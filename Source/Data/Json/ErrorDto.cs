using System.Runtime.Serialization;

namespace RimTalk.Data;

[DataContract]
public class ErrorResponse
{
    [DataMember(Name = "error")]
    public ErrorDetail Error { get; set; }
}

[DataContract]
public class ErrorDetail
{
    [DataMember(Name = "code")]
    public int Code { get; set; }

    [DataMember(Name = "message")]
    public string Message { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; }
    
    [DataMember(Name = "type")]
    public string Type { get; set; }
}