using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimTalk.Client.OpenAI
{
    [DataContract]
    public class ModelsResponse
    {
        [DataMember(Name = "data")]
        public List<Model> Data { get; set; }
    }

    [DataContract]
    public class Model
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }
    }
}
