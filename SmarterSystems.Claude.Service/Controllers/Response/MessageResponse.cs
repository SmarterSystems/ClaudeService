namespace SmarterSystems.Claude.Service.Controllers.Response
{
    public class MessageResponse : ResponseBase
    {
        public string Message { get; set; }
        public string StopReason { get; set; }
    }
}
