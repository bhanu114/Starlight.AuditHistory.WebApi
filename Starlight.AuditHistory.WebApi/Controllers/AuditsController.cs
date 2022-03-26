/// <summary>
/// Starlight.AuditHistory.WebApi.Controllers
/// </summary>
namespace Starlight.AuditHistory.WebApi.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.ServiceModel.Description;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;
    using System.Text;
    using System.Web.Http;
    //using System.Web.Script.Serialization;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.WebServiceClient;
    using Microsoft.Xrm.Sdk.Client;
    using Microsoft.Xrm.Sdk.Query;
    using Models;
    using Newtonsoft.Json;
    using System.Linq;
    using System.Web.Script.Serialization;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Metadata;

    /// <summary>
    /// Class for Audit history
    /// </summary>
    public class AuditsController : ApiController
    {
        /// <summary>
        /// The IOrganization Service
        /// </summary>
        private IOrganizationService organizationService; 
        string EntityLogicalName = "";
        string EntiryDisplayName = "";
        private static Dictionary<string, string> AttrFullNameDispNameMap = new Dictionary<string, string>();
        private RetrieveEntityResponse EntityMetaResponse;
        private DateTime startDate_search;
        private DateTime endDate_search;
        private Guid? id_search;
        private string entityName_search;
        private string searchString;
        /// <summary>
        /// The DataTable
        /// </summary>
        private DataTable dtcombined = null;

        /// <summary>
        /// The DataTable
        /// </summary>
        private DataTable dtAzureCombined = null;

        /// <summary>
        /// GetAuditHistory
        /// </summary>
        /// <param name="ObjectId">object id</param>
        /// <param name="entityName">entity Name</param>
        /// <returns>The json data</returns>
        [Route("api/audits/{id}/{entityName}")]
        public HttpResponseMessage Get(Guid? id, string entityName)
        {
            try
            {
                this.InitializeComponents();
                //QUERY DATA FROM AZURE TABLE

                List<AuditDetails> lstAzureDetails = this.GetAuditDetailsFromAzure(id, entityName);
                //QUERY DATA FROM CRM
                List<AuditDetails> lstDetails = this.GetAuditDetailsFromCRM(id, entityName);
                lstDetails.AddRange(lstAzureDetails);
                lstDetails = lstDetails.OrderByDescending(x => x.ChangedDate).ToList();
                var strJson = JsonConvert.SerializeObject(lstDetails);
                if (!string.IsNullOrEmpty(strJson))
                {
                    var response = Request.CreateResponse(HttpStatusCode.OK);
                    response.Content = new StringContent(strJson, Encoding.UTF8, "application/json");
                    return response;
                }
                else
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                throw new WebException(ex.Message);
            }
        }
        public static void ValidateFieldSecurity()
        {
           

            // Instantiate QueryExpression query
            var query = new QueryExpression("fieldsecurityprofile");
            query.TopCount = 50;
            // Add all columns to query.ColumnSet
            query.ColumnSet.AllColumns = true;
            // Add link-entity query_systemuserprofiles
            var query_systemuserprofiles = query.AddLink("systemuserprofiles", "fieldsecurityprofileid", "fieldsecurityprofileid");
            // Add all columns to query_systemuserprofiles.Columns
            query_systemuserprofiles.Columns.AllColumns = true;
        
        
        
        }
        [Route("api/audits/{id}/{entityName}/{searchString}")]
        public HttpResponseMessage GetByDate(Guid? id, string entityName, string searchString)
        {
            Console.WriteLine("******************* ");
            this.searchString = searchString;
            id_search = id;
            entityName_search = entityName;
            HttpResponseMessage result = Get(id_search, entityName);
            //ProcessRequest();


            return result;
        }
        [Route("api/audits/{id}/{entityName}/{startDateStr}/{endDateStr}")]
        public HttpResponseMessage GetByDate(Guid? id, string entityName, string startDateStr, string endDateStr)
        {
            Console.WriteLine("******************* ");
            startDate_search =  Convert.ToDateTime("11/1/2022 12:00:00 AM"); // 1/1/0001 12:00:00 AM
            endDate_search = Convert.ToDateTime("11/1/2022 12:00:00 AM"); // 1/1/0001 12:00:00 AM
            id_search = id;
            entityName_search = entityName;
            ProcessRequest();


            return null;
        }
        public HttpResponseMessage ProcessRequest()
        {
            try
            {
                this.InitializeComponents();
                //QUERY DATA FROM AZURE TABLE

                List<AuditDetails> lstAzureDetails = this.GetAuditDetailsFromAzure(id_search, entityName_search);
                //QUERY DATA FROM CRM
                List<AuditDetails> lstDetails = this.GetAuditDetailsFromCRM(id_search, entityName_search);
                lstDetails.AddRange(lstAzureDetails);
                lstDetails = lstDetails.OrderByDescending(x => x.ChangedDate).ToList();
                var strJson = JsonConvert.SerializeObject(lstDetails);
                if (!string.IsNullOrEmpty(strJson))
                {
                    var response = Request.CreateResponse(HttpStatusCode.OK);
                    response.Content = new StringContent(strJson, Encoding.UTF8, "application/json");
                    return response;
                }
                else
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }
            }
            catch (Exception ex)
            {
                throw new WebException(ex.Message);
            }

            return null;
        }
            /// <summary>
            /// This is to Initialize the Components
            /// </summary>
            private void InitializeComponents()
        {
            this.dtcombined = this.CreateCombinedDataTable();
            this.dtAzureCombined = this.CreateCombinedDataTable();
            this.CreateCRMConnection();
        }

        /// <summary>
        /// This is to get the audit details from azure
        /// </summary>
        /// <param name="objectId">the object id</param>
        /// <param name="entityName">entity Name</param>
        /// <returns>The List of Audit Details</returns>
        private List<AuditDetails> GetAuditDetailsFromAzure(Guid? objectId, string entityName)
        {
            var strOldValue = string.Empty;
            string storageAccount = ConfigurationManager.AppSettings["StorageAccount"];
            string accessKey = ConfigurationManager.AppSettings["AccessKey"];
            string resourcePath = "AuditDetailCollection()?$filter=(ObjectId%20eq%20guid'" + objectId + "')" + "and" + "(ObjectTypeCode%20eq%20'" + entityName + "')";
            
            //construct query request based on available inputs
            if (startDate_search != null)
            {
                resourcePath += "and" + "(CreatedOn%20ge%20'" + startDate_search + "')";
            }
            if (endDate_search != null)
            {
                resourcePath += "and" + "(CreatedOn%20le%20'" + endDate_search + "')";
            }
            if (searchString != null)
            {
                resourcePath += "and" + "(CreatedOn%20le%20'" + searchString + "')";
            }
            string jsonData = string.Empty;
            var iStatus = this.RequestResource(storageAccount, accessKey, resourcePath, out jsonData);
            var result = JsonConvert.DeserializeObject<AzureRootObject>(jsonData);
            List<AuditDetails> lstAzureDetails = GetAttributeAuditDetailsFromAzure(ref strOldValue, storageAccount, accessKey, result);
            return lstAzureDetails;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="strOldValue"></param>
        /// <param name="storageAccount"></param>
        /// <param name="accessKey"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        private List<AuditDetails> GetAttributeAuditDetailsFromAzure(ref string strOldValue, string storageAccount, string accessKey, AzureRootObject result)
        {
            if (result != null)
            {
              
                //PROCESS EACH AUDIT RECORD : 
                foreach (var item in result.value)
                {
                    string strResourcePath = "AuditAttributeDetailCollection()?$filter=AuditId%20eq%20guid'" + item.AuditId + "'";
                    string strJsonData = string.Empty;
                    var iAuditStatus = this.RequestResource(storageAccount, accessKey, strResourcePath, out strJsonData);
                    var results = JsonConvert.DeserializeObject<AzureAttributeRoot>(strJsonData);
                    strOldValue = Convert.ToString(item.CreatedOn);

                    //FOR EACH AUDIT(TRANSACTION) RECORD FETCH ALL ATTRIBUTE CHANGED VALUES
                    foreach (var auditdetail in results.value)
                    {
                        //Console.WriteLine('>>>>>>>>> auditdetail = ' + auditdetail);
                        DataRow dtAzureValues = this.dtAzureCombined.NewRow();
                        dtAzureValues["AuditId"] = auditdetail.AuditId;
                        dtAzureValues["ChangedDate"] = Convert.ToString(item.CreatedOn);
                        dtAzureValues["Event"] = item.ActionName;
                        dtAzureValues["ChangedBy"] = item.UserName;
                        dtAzureValues["ChangedField"] = auditdetail.AttributeDisplayName;
                        if (auditdetail.OldValue != "(no value)")
                        {
                            dtAzureValues["OldValue"] = auditdetail.OldValue;
                        }
                        else
                        {
                            dtAzureValues["OldValue"] = string.Empty;
                        }
                        if (auditdetail.NewValue != "(no value)")
                        {
                            dtAzureValues["NewValue"] = auditdetail.NewValue;
                        }
                        else
                        {
                            dtAzureValues["NewValue"] = string.Empty;
                        }
                        this.dtAzureCombined.Rows.Add(dtAzureValues);
                    }
                }
            }
            var str = DataTableToJSONWithJavaScriptSerializer(dtAzureCombined);
            List<AuditDetails> lstAzureDetails = new List<AuditDetails>();
            lstAzureDetails = this.ConvertDataTableToList<AuditDetails>(this.dtAzureCombined);
            return lstAzureDetails;
        }

        /// <summary>
        /// This is to get the audit details from CRM
        /// </summary>
        /// <param name="objectId">the object id</param>
        /// <returns>The List of Audit details</returns>
        private List<AuditDetails> GetAuditDetailsFromCRM(Guid? objectId, string entityName)
        {
            QueryExpression query = new QueryExpression("audit");
            query.ColumnSet = new ColumnSet(true);
            ConditionExpression objectTypeCheck = new ConditionExpression("objectid", ConditionOperator.Equal, objectId);
            ConditionExpression entityNameCheck = new ConditionExpression("objecttypecode", ConditionOperator.Equal, entityName);
           
            FilterExpression queryFilterExp = new FilterExpression(LogicalOperator.And);
            queryFilterExp.Conditions.AddRange(objectTypeCheck);
            queryFilterExp.Conditions.AddRange(entityNameCheck);
            if (searchString != null)
            {
                ConditionExpression fieldNameCheck = new ConditionExpression("objecttypecode", ConditionOperator.Equal, entityName);
                queryFilterExp.Conditions.AddRange(fieldNameCheck);
            }
            query.Criteria.AddFilter(queryFilterExp);
            query.AddOrder("createdon", OrderType.Descending);
            EntityCollection auditDetails = this.organizationService.RetrieveMultiple(query);
            List<AuditDetails> lstDetails = new List<AuditDetails>();
            if (auditDetails != null && auditDetails.Entities.Count > 0)
            {
                foreach (var ent in auditDetails.Entities)
                {
                    var auditDetailsRequest = new RetrieveAuditDetailsRequest
                    {
                        AuditId = ent.Id
                    };
                    var auditDetailsResponse = (RetrieveAuditDetailsResponse)this.organizationService.Execute(auditDetailsRequest);
                    //GET TABLE LABLE
                    EntityLogicalName = auditDetailsResponse.AuditDetail.AuditRecord.GetAttributeValue<string>("objecttypecode");
                    EntityMetaResponse = getRetrieveEntityResponse(EntityLogicalName);
                    EntiryDisplayName = EntityMetaResponse.EntityMetadata.DisplayName.LocalizedLabels[0].Label;
                    this.FetchAuditDetails(auditDetailsResponse.AuditDetail);
                }

                DataTable dtCom = this.dtcombined;
                lstDetails = ConvertDataTableToList<AuditDetails>(dtCom);
            }

            return lstDetails;
        }

        /// <summary>
        /// This to convert the data table to list
        /// </summary>
        /// <typeparam name="T">The audit details</typeparam>
        /// <param name="dt">The data table</param>
        /// <returns>The List Of Audit Details</returns>
        private List<T> ConvertDataTableToList<T>(DataTable dt)
        {
            List<T> data = new List<T>();
            foreach (DataRow row in dt.Rows)
            {
                T item = this.GetItem<T>(row);
                data.Add(item);
            }

            return data;
        }

        /// <summary>
        /// This is to get the property value
        /// </summary>
        /// <typeparam name="T">The audit details</typeparam>
        /// <param name="dr">The Data row</param>
        /// <returns>The Row Value</returns>
        private T GetItem<T>(DataRow dr)
        {
            Type temp = typeof(T);
            T obj = Activator.CreateInstance<T>();

            foreach (DataColumn column in dr.Table.Columns)
            {
                foreach (PropertyInfo pro in temp.GetProperties())
                {
                    if (pro.Name == column.ColumnName)
                    {
                        pro.SetValue(obj, dr[column.ColumnName], null);
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            return obj;
        }

        /// <summary>
        /// This is to create the combined data table
        /// </summary>
        /// <returns>The data table</returns>
        private DataTable CreateCombinedDataTable()
        {
            DataTable dt = new DataTable("AuditDetailCollection");
            dt.Columns.Add("AuditId", typeof(Guid));
            dt.Columns.Add("ChangedDate", typeof(string));
            dt.Columns.Add("Event", typeof(string));
            dt.Columns.Add("ChangedBy", typeof(string));
            dt.Columns.Add("ChangedField", typeof(string));
            dt.Columns.Add("OldValue", typeof(string));
            dt.Columns.Add("NewValue", typeof(string));
            return dt;
        }

        /// <summary>
        /// This is to Fetch the Audit details
        /// </summary>
        /// <param name="detail">The Audit Detail</param>
        private void FetchAuditDetails(AuditDetail detail)
        {
            int counter = 0;
            List<AuditDetails> lstDetails = new List<AuditDetails>();
            var detailType = detail.GetType();
            if (detailType == typeof(AttributeAuditDetail))
            {
                var attributeDetail = (AttributeAuditDetail)detail;
                string AttributeDisplayName = "";
                if (attributeDetail.NewValue != null)
                {
                    foreach (KeyValuePair<string, object> attribute in attributeDetail.NewValue.Attributes)
                    {
                        counter = counter + 1;
                        string oldValue = string.Empty, newValue = string.Empty;
                        //*********************GET AttributeDisplayName 
                        string AttrFullLogicalName = EntityLogicalName + ":" + attribute.Key;
                        if (!AttrFullNameDispNameMap.ContainsKey(AttrFullLogicalName)) { 
                            for (int iCnt = 0; iCnt < EntityMetaResponse.EntityMetadata.Attributes.ToList().Count; iCnt++)
                            {
                                if (EntityMetaResponse.EntityMetadata.Attributes.ToList()[iCnt].DisplayName.LocalizedLabels.Count > 0)
                                {
                                    if (EntityMetaResponse.EntityMetadata.Attributes.ToList()[iCnt].LogicalName == attribute.Key)
                                    {
                                        AttributeDisplayName = EntityMetaResponse.EntityMetadata.Attributes.ToList()[iCnt].DisplayName.LocalizedLabels[0].Label;
                                        if (AttributeDisplayName == "" || AttributeDisplayName == null)
                                        {
                                            Console.WriteLine("################ Attribute Display name is empty ");
                                        }
                                        AttrFullNameDispNameMap.Add(AttrFullLogicalName, AttributeDisplayName);
                                        string logicalName = EntityMetaResponse.EntityMetadata.Attributes.ToList()[iCnt].LogicalName;
                                        break;
                                    }
                                }
                            }
                        }else
                        {
                            AttrFullNameDispNameMap.TryGetValue(AttrFullLogicalName, out AttributeDisplayName);
                        }
                        //************************************
                        if (attributeDetail.OldValue.Contains(attribute.Key))
                        {
                            oldValue = attributeDetail.OldValue[attribute.Key].ToString();
                            if (oldValue == "Microsoft.Xrm.Sdk.OptionSetValue" || oldValue == "Microsoft.Xrm.Sdk.EntityReference" || oldValue == "Microsoft.Xrm.Sdk.Money")
                            {
                                if (attributeDetail.OldValue.FormattedValues.Keys.Contains(attribute.Key))
                                {
                                    oldValue = attributeDetail.OldValue.FormattedValues[attribute.Key];
                                }
                                else
                                {
                                    oldValue = "Record Unavailable";
                                }
                            }
                        }
                        if (attributeDetail.NewValue.Contains(attribute.Key))
                        {
                            newValue = attributeDetail.NewValue[attribute.Key].ToString();
                            if (newValue == "Microsoft.Xrm.Sdk.OptionSetValue" || newValue == "Microsoft.Xrm.Sdk.EntityReference" || newValue == "Microsoft.Xrm.Sdk.Money")
                            {
                                if (attributeDetail.NewValue.FormattedValues.Keys.Contains(attribute.Key))
                                {
                                    newValue = attributeDetail.NewValue.FormattedValues[attribute.Key];
                                }
                                else
                                {
                                    newValue = "Record Unavailable";
                                }
                            }
                        }
                        DataRow dr = this.dtcombined.NewRow();
                        dr["AuditId"] = detail.AuditRecord.Id;
                        dr["ChangedDate"] = Convert.ToString(detail.AuditRecord.GetAttributeValue<DateTime>("createdon"));
                        dr["Event"] = detail.AuditRecord.FormattedValues["operation"];
                        dr["ChangedBy"] = detail.AuditRecord.GetAttributeValue<EntityReference>("userid").Name;
                        dr["ChangedField"] = AttributeDisplayName;// attribute.Key;
                        dr["OldValue"] = oldValue;
                        dr["NewValue"] = newValue;
                        this.dtcombined.Rows.Add(dr);
                    }
                }
                else
                {
                    if (attributeDetail.OldValue != null)
                    {
                        foreach (KeyValuePair<string, object> attribute in attributeDetail.OldValue.Attributes)
                        {
                            string oldValue = string.Empty;
                            if (attributeDetail.OldValue.Contains(attribute.Key))
                            {
                                if (attributeDetail.OldValue.FormattedValues.Keys.Contains(attribute.Key))
                                {
                                    oldValue = attributeDetail.OldValue.FormattedValues[attribute.Key];
                                }
                                else
                                {
                                    oldValue = "Record Unavailable";
                                }
                            }
                            DataRow dr = this.dtcombined.NewRow();
                            dr["AuditId"] = detail.AuditRecord.Id;
                            dr["ChangedDate"] = Convert.ToString(detail.AuditRecord.GetAttributeValue<DateTime>("createdon"));
                            dr["Event"] = detail.AuditRecord.FormattedValues["action"];
                            dr["ChangedBy"] = detail.AuditRecord.GetAttributeValue<EntityReference>("userid").Name;
                            dr["ChangedField"] = AttributeDisplayName;// attribute.Key;
                            dr["OldValue"] = oldValue;
                            dr["NewValue"] = string.Empty;
                            this.dtcombined.Rows.Add(dr);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This is to establish the connection to CRM
        /// </summary>
        private void CreateCRMConnection()
        {
            try
            {
                string clientId = ConfigurationManager.AppSettings["CLENT_ID"];
                string clientSecret = ConfigurationManager.AppSettings["CLIENT_SECRET"];
                ClientCredential credentials = new ClientCredential(clientId, clientSecret);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string domain = ConfigurationManager.AppSettings["DOMAIN"];
                string url = $"https://{domain}.api.crm.dynamics.com";
                OrganizationServiceContext xrmContext;
                OrganizationWebProxyClient webProxyClient;

                AuthenticationParameters authParam = AuthenticationParameters.CreateFromUrlAsync(new Uri(url + "/api/data/")).Result;// CreateFromResourceUrlAsync(new Uri(url + "/api/data/")).Result;
                var authority = authParam.Authority.Replace(@"oauth2/authorize", "");
                AuthenticationContext authContext = new AuthenticationContext(authority, false);
                AuthenticationResult authenticationResult = authContext.AcquireTokenAsync(url, credentials).Result;
                webProxyClient = new OrganizationWebProxyClient(new Uri(url + @"/xrmservices/2011/organization.svc/web?SdkClientVersion=8.2"), false);//TODO:Change version to 9.2
                webProxyClient.HeaderToken = authenticationResult.AccessToken;
                this.organizationService = (IOrganizationService)webProxyClient;  //new OrganizationServiceProxy(new Uri(serviceUrl), null, credentials, null);
                Guid orgId = ((WhoAmIResponse)this.organizationService.Execute(new WhoAmIRequest())).OrganizationId;
                Console.WriteLine("User Login Success");
            }
            catch (Exception ex)
            {
                throw new WebException(ex.Message);
            }
        }

        /// <summary>
        /// This is to send request to azure
        /// </summary>
        /// <param name="storageAccount">The Storage Account</param>
        /// <param name="accessKey">The Access Key</param>
        /// <param name="resourcePath">The Resource Path</param>
        /// <param name="jsonData">The json Data</param>
        /// <returns>json Data</returns>
        private int RequestResource(string storageAccount, string accessKey, string resourcePath, out string jsonData)
        {
            string uri = @"https://" + storageAccount + ".table.core.windows.net/" + resourcePath;
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(uri);
            request.Method = "GET";
            request.ContentType = "application/json";
            request.ContentLength = 0;
            request.Accept = "application/json;odata=nometadata";
            request.Headers.Add("x-ms-date", DateTime.UtcNow.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("x-ms-version", "2015-12-11");
            request.Headers.Add("Accept-Charset", "UTF-8");
            request.Headers.Add("MaxDataServiceVersion", "3.0;NetFx");
            request.Headers.Add("DataServiceVersion", "3.0;NetFx");
            string stringToSign = request.Headers["x-ms-date"] + "\n";
            int query = resourcePath.IndexOf("?");
            var resourcePathString = string.Empty;
            if (query > 0)
            {
                resourcePathString = resourcePath.Substring(0, query);
            }

            stringToSign += "/" + storageAccount + "/" + resourcePathString;
            //string testaccessKey = accessKey.Replace("_", "/").Replace("-", "+") + new string('=', (4 - accessKey.Length % 4) % 4);

            System.Security.Cryptography.HMACSHA256 hasher = new System.Security.Cryptography.HMACSHA256(Convert.FromBase64String(accessKey));
            string strAuthorization = "SharedKeyLite " + storageAccount + ":" + System.Convert.ToBase64String(hasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stringToSign)));
            request.Headers.Add("Authorization", strAuthorization);
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (System.IO.StreamReader r = new System.IO.StreamReader(response.GetResponseStream()))
                    {
                        jsonData = r.ReadToEnd();
                        return (int)response.StatusCode;
                    }
                }
            }
            catch (WebException ex)
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(ex.Response.GetResponseStream()))
                {
                    jsonData = sr.ReadToEnd();
                }

                return (int)ex.Status;
            }
        }

        public string DataTableToJSONWithJavaScriptSerializer(DataTable table)
        {
            JavaScriptSerializer jsSerializer = new JavaScriptSerializer();
            List<Dictionary<string, object>> parentRow = new List<Dictionary<string, object>>();
            Dictionary<string, object> childRow;
            foreach (DataRow row in table.Rows)
            {
                childRow = new Dictionary<string, object>();
                foreach (DataColumn col in table.Columns)
                {
                    childRow.Add(col.ColumnName, row[col]);
                }
                parentRow.Add(childRow);
            }
            return jsSerializer.Serialize(parentRow);
        }

        public string DecodePassword(string encodedData)
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            System.Text.Decoder utf8Decode = encoder.GetDecoder();
            byte[] todecode_byte = Convert.FromBase64String(encodedData);
            int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount];
            utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
            string result = new String(decoded_char);
            return result;
        }
        public RetrieveEntityResponse getRetrieveEntityResponse(string EntityLogicalName)
        {
            RetrieveEntityRequest req = new RetrieveEntityRequest();
            req.RetrieveAsIfPublished = true;
            req.LogicalName = EntityLogicalName;
            req.EntityFilters = EntityFilters.Attributes;

            RetrieveEntityResponse resp = (RetrieveEntityResponse)  organizationService.Execute(req);

            return resp;
        }
    }
}
