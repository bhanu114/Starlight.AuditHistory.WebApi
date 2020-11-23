using System;
using System.Collections.Generic;

namespace Starlight.AuditHistory.WebApi.Models
{
    public class AuditDetails
    {
        public Guid? AuditId { get; set; }
        public string ChangedField { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Event { get; set; }
        public string ChangedBy { get; set; }
        public string ChangedDate { get; set; }

    }
    public class AzureValue
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTime Timestamp { get; set; }
        public string ActionName { get; set; }
        public string AuditId { get; set; }
        public DateTime CreatedOn { get; set; }
        public string Name { get; set; }
        public string ObjectId { get; set; }
        public string ObjectTypeCode { get; set; }
        public string Operation { get; set; }
        public string UserName { get; set; }
    }

    public class CRMRootObject
    {
        public List<AuditDetails> value { get; set; }
    }

    public class AzureRootObject
    {
        public List<AzureValue> value { get; set; }
    }
    public class AzureAttributeValue
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTime Timestamp { get; set; }
        public int RowNumber { get; set; }
        public string AuditId { get; set; }
        public string AttributeName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class AzureAttributeRoot
    {
        public List<AzureAttributeValue> value { get; set; }
    }
}