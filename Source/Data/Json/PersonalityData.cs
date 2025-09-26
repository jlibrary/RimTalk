using System.Runtime.Serialization;

namespace RimTalk.Data;

[DataContract]
public class PersonalityData : IJsonData
{
    [DataMember(Name = "persona")] public string Persona { get; set; }

    [DataMember(Name = "chattiness")] public float Chattiness { get; set; } = 1.0f;

    public override string ToString()
    {
        return Persona;
    }
}