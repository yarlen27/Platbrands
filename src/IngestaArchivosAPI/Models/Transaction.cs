using Newtonsoft.Json;

namespace IngestaArchivosAPI.Models
{
    public class Transaction
    {
        [JsonProperty("patient_id")]
        public string? PatientId { get; set; }

        [JsonProperty("patient_name")]
        public string? PatientName { get; set; }

        [JsonProperty("insurance_company")]
        public string? InsuranceCompany { get; set; }

        [JsonProperty("check_amount")]
        public decimal? CheckAmount { get; set; }

        [JsonProperty("payment_type")]
        public string? PaymentType { get; set; }


        [JsonProperty("posted_amount")]
        public decimal? PostedAmount { get; set; }

        [JsonProperty("check_number")]
        public string? CheckNumber { get; set; }

        [JsonProperty("service_date")]
        public string? ServiceDate { get; set; }

        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("other_amount")]
        public decimal? OtherAmount { get; set; }
    }

}