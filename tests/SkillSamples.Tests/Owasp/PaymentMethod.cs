using System.Text.Json.Serialization;

namespace SkillSamples.Owasp;

// Polymorphic JSON with System.Text.Json: the discriminator picks from this list and nothing else. An unknown
// "kind" is a 400, never an instance of some other type. (Newtonsoft's TypeNameHandling other than None reads
// a .NET type name from the request and creates it: a remote code execution route.)
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CardPayment), "card")]
[JsonDerivedType(typeof(InsurancePayment), "insurance")]
public abstract record PaymentMethod;

public sealed record CardPayment(string Last4) : PaymentMethod;

public sealed record InsurancePayment(string PolicyNumber) : PaymentMethod;
