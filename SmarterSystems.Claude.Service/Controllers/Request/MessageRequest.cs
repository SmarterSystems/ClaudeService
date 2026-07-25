namespace SmarterSystems.Claude.Service.Controllers.Request
{
    public class MessageRequest
    {
        public Guid SessionId { get; set; }
        public string FileId { get; set; }
        public string Message { get; set; }
        public string Schema { get; set; }
        public string Model { get; set; }
    }
}
