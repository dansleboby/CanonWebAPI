namespace Canon.API.Infrastructure;

/// <summary>
/// Marks an endpoint that answers even when the camera is not connected or busy: the 503 response declared for the
/// whole controller is removed from its OpenAPI description.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NeverUnavailableAttribute : Attribute;
