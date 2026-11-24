namespace TyHr.Domain;

/// <summary>Raised when a business rule is violated. The API returns it as a 400 with the message.</summary>
public class DomainException(string message) : Exception(message);
