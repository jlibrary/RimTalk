using System.Runtime.Serialization;

namespace RimTalk.Data;

[DataContract]
public class PersonalityData(string persona, float chattiness = 1.0f) : IJsonData
{
    [DataMember(Name = "persona")] public string Persona { get; set; } = persona;

    [DataMember(Name = "chattiness")] public float Chattiness { get; set; } = chattiness;

    public override string ToString()
    {
        return Persona;
    }
}