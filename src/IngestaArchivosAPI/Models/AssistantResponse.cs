namespace IngestaArchivosAPI.Models
{
   

    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Attachment
    {
        public string file_id { get; set; }
        public List<Tool> tools { get; set; }
    }

    public class Content
    {
        public string type { get; set; }
        public Text text { get; set; }
    }

    public class Datum
    {
        public string id { get; set; }
        public string @object { get; set; }
        public int created_at { get; set; }
        public string assistant_id { get; set; }
        public string thread_id { get; set; }
        public string run_id { get; set; }
        public string role { get; set; }
        public List<Content> content { get; set; }
        public List<Attachment> attachments { get; set; }
        public Metadata metadata { get; set; }
    }

    public class Metadata
    {
    }

    public class AssistantResponse
    {
        public string @object { get; set; }
        public List<Datum> data { get; set; }
        public string first_id { get; set; }
        public string last_id { get; set; }
        public bool has_more { get; set; }
    }

    public class Text
    {
        public string value { get; set; }
        public List<object> annotations { get; set; }
    }

    public class Tool
    {
        public string type { get; set; }
    }


}
