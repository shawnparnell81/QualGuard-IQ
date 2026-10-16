using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Security;

/// <summary>A role is a named, configurable set of permissions. Roles are data, never hard-coded checks.</summary>
public class Role : Entity, IAudited
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public List<RolePermission> Permissions { get; set; } = [];
    public string AuditLabel => $"Role {Name}";
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public string Permission { get; set; } = "";
}

/// <summary>Grants a role to a user, optionally limited to one site and/or department. Null scope means all.</summary>
public class UserRoleAssignment : Entity, IAudited
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"Role assignment {RoleId}";
}

/// <summary>Security events (sign-in, lockout, denial). Kept separate from the QMS business audit trail.</summary>
public class SecurityEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Succeeded { get; set; }
    public string? Detail { get; set; }
}
