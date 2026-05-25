namespace CognitivePlatform.Api.Registry.Domains;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DomainAttribute : Attribute
{
    public Type DomainType { get; }
    public DomainAttribute(Type domainType) => DomainType = domainType;
}
