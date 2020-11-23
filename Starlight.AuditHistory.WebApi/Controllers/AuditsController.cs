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
    using System.Text;
    using System.Web.Http;
    using System.Web.Script.Serialization;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Client;
    using Microsoft.Xrm.Sdk.Query;
    using Models;
    using Newtonsoft.Json;
    using System.Linq;

    /// <summary>
    /// Class for Audit history
    /// </summary>
    public class AuditsController : ApiController
    {
        /// <summary>
        /// The IOrganization Service
        /// </summary>
        private IOrganizationService organizationService;

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
                List<AuditDetails> lstAzureDetails = this.GetAuditDetailsFromAzure(id, entityName);
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
                foreach (var item in result.value)
                {
                    string strResourcePath = "AuditAttributeDetailCollection()?$filter=AuditId%20eq%20guid'" + item.AuditId + "'";
                    string strJsonData = string.Empty;
                    var iAuditStatus = this.RequestResource(storageAccount, accessKey, strResourcePath, out strJsonData);
                    var results = JsonConvert.DeserializeObject<AzureAttributeRoot>(strJsonData);
                    strOldValue = Convert.ToString(item.CreatedOn);

                    foreach (var auditdetail in results.value)
                    {
                        DataRow dtAzureValues = this.dtAzureCombined.NewRow();
                        dtAzureValues["AuditId"] = auditdetail.AuditId;
                        dtAzureValues["ChangedDate"] = Convert.ToString(item.CreatedOn);
                        dtAzureValues["Event"] = item.ActionName;
                        dtAzureValues["ChangedBy"] = item.UserName;
                        dtAzureValues["ChangedField"] = auditdetail.AttributeName;
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
            query.Criteria.AddFilter(queryFilterExp);
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
                if (attributeDetail.NewValue != null)
                {
                    foreach (KeyValuePair<string, object> attribute in attributeDetail.NewValue.Attributes)
                    {
                        counter = counter + 1;
                        string oldValue = string.Empty, newValue = string.Empty;
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
                        dr["ChangedField"] = attribute.Key;
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
                            dr["ChangedField"] = attribute.Key;
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
                ClientCredentials credentials = new ClientCredentials();
                credentials.UserName.UserName = ConfigurationManager.AppSettings["UserName"];
                string password = ConfigurationManager.AppSettings["Password"];
                credentials.UserName.Password = this.DecodePassword(password);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var serviceUrl = ConfigurationManager.AppSettings["ServiceUrl"];
                this.organizationService = (IOrganizationService)new OrganizationServiceProxy(new Uri(serviceUrl), null, credentials, null);
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
            request.Headers.Add("x-ms-version", "2015-04-05");
            request.Headers.Add("Accept-Charset", "UTF-8");
            request.Headers.Add("MaxDataServiceVersion", "3.0;NetFx");
            request.Headers.Add("DataServiceVersion", "1.0;NetFx");
            string stringToSign = request.Headers["x-ms-date"] + "\n";
            int query = resourcePath.IndexOf("?");
            var resourcePathString = string.Empty;
            if (query > 0)
            {
                resourcePathString = resourcePath.Substring(0, query);
            }

            stringToSign += "/" + storageAccount + "/" + resourcePathString;
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

    }
}
