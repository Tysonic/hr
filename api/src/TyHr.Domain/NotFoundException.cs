namespace TyHr.Domain;

/// <summary>Raised when a requested record does not exist (or belongs to another company). The API returns a 404.</summary>
public class NotFoundException(string message) : Exception(message);
