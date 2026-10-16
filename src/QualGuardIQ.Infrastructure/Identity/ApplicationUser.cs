using Microsoft.AspNetCore.Identity;

namespace QualGuardIQ.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid? DefaultSiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
}
