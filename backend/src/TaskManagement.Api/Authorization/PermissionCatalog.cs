using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authorization;

/// <summary>Permission keys used by implemented endpoints. Keys are stable API: never rename a shipped key.</summary>
public static class PermissionKeys
{
    public static class Members
    {
        public const string Access = "members.access";
        public const string View = "members.view";
        public const string ViewDetails = "members.view_details";
        public const string Create = "members.create";
        public const string Edit = "members.edit";
        public const string ChangeEmail = "members.change_email";
        public const string Delete = "members.delete";
        public const string Activate = "members.activate";
        public const string Deactivate = "members.deactivate";
        public const string ChangeRole = "members.change_role";
        public const string ResetPassword = "members.reset_password";
        public const string ViewPersonalInformation = "members.view_personal_information";
        public const string EditPersonalInformation = "members.edit_personal_information";
        public const string Export = "members.export";
        public const string ManagePermissions = "members.manage_permissions";
        public const string ViewAudit = "members.view_audit";
    }

    public static class Roles
    {
        public const string Access = "roles.access";
        public const string View = "roles.view";
        public const string Create = "roles.create";
        public const string Edit = "roles.edit";
        public const string Delete = "roles.delete";
        public const string ManagePermissions = "roles.manage_permissions";
        public const string ViewAudit = "roles.view_audit";
    }
}

public sealed record ModuleDefinition(string Key, string Name, string Icon, bool IsImplemented, int SortOrder);

public sealed record PermissionDefinition(
    int Id, string Key, string DisplayName, string Description, string ModuleKey, bool IsTabAccess, bool AllowMemberOverride);

/// <summary>
/// The controlled registry of every permission. To add permissions for a new module: add the module and its
/// permissions here (new, never-reused ids), mark the module implemented when its endpoints exist, and add an
/// EF Core migration so the rows are seeded. Ids and keys must never be reused.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>Fixed creation timestamp for seeded rows (seed data must be deterministic).</summary>
    public static readonly DateTime SeededAtUtc = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<ModuleDefinition> Modules =
    [
        new("dashboard", "Dashboard", "bi-speedometer2", false, 10),
        new("members", "Member Management", "bi-people", true, 20),
        new("roles", "Roles & Permissions", "bi-shield-lock", true, 30),
        new("projects", "Projects", "bi-folder2-open", false, 40),
        new("tasks", "Tasks", "bi-list-check", false, 50),
        new("meetings", "Meetings", "bi-journal-text", false, 60),
        new("milestones", "Milestones", "bi-flag", false, 70),
        new("settings", "Global Settings", "bi-gear", false, 80),
    ];

    public static readonly IReadOnlyList<PermissionDefinition> Definitions =
    [
        // Dashboard (not implemented yet)
        new(100, "dashboard.access", "Tab Access", "Show the Dashboard tab.", "dashboard", true, true),
        new(101, "dashboard.view", "View Dashboard", "View dashboard widgets.", "dashboard", false, true),
        new(102, "dashboard.export", "Export Dashboard", "Export dashboard data.", "dashboard", false, true),

        // Member Management
        new(200, PermissionKeys.Members.Access, "Tab Access", "Show the Members tab.", "members", true, true),
        new(201, PermissionKeys.Members.View, "View Member List", "Search and list members.", "members", false, true),
        new(202, PermissionKeys.Members.ViewDetails, "View Member Details", "Open a member's account details.", "members", false, true),
        new(203, PermissionKeys.Members.Create, "Create Member", "Create member accounts with roles the user may assign.", "members", false, true),
        new(204, PermissionKeys.Members.Edit, "Edit Member", "Edit a member's name and username.", "members", false, true),
        new(205, PermissionKeys.Members.ChangeEmail, "Change Member Email", "Change a member's email address.", "members", false, true),
        new(206, PermissionKeys.Members.Delete, "Delete Member", "Permanently delete deactivated members.", "members", false, true),
        new(207, PermissionKeys.Members.Activate, "Activate Member", "Re-activate a deactivated member.", "members", false, true),
        new(208, PermissionKeys.Members.Deactivate, "Deactivate Member", "Deactivate a member and end their sessions.", "members", false, true),
        new(209, PermissionKeys.Members.ChangeRole, "Change Member Role", "Assign a different role to a member.", "members", false, false),
        new(210, PermissionKeys.Members.ResetPassword, "Reset Member Password", "Send a member a password reset or setup link.", "members", false, true),
        new(211, PermissionKeys.Members.ViewPersonalInformation, "View Personal Information", "See date of birth, phone and address.", "members", false, true),
        new(212, PermissionKeys.Members.EditPersonalInformation, "Edit Personal Information", "Change date of birth, phone, address and photo.", "members", false, true),
        new(213, PermissionKeys.Members.Export, "Export Member Information", "Download the member list as CSV.", "members", false, true),
        new(214, PermissionKeys.Members.ManagePermissions, "Manage Member Permissions", "Set member-specific permission overrides.", "members", false, false),
        new(215, PermissionKeys.Members.ViewAudit, "View Member History", "See the audit history of a member.", "members", false, true),

        // Roles & Permissions
        new(300, PermissionKeys.Roles.Access, "Tab Access", "Show the Roles & Permissions tab.", "roles", true, true),
        new(301, PermissionKeys.Roles.View, "View Roles", "List roles and see their permissions.", "roles", false, true),
        new(302, PermissionKeys.Roles.Create, "Create Role", "Create custom roles.", "roles", false, false),
        new(303, PermissionKeys.Roles.Edit, "Edit Role", "Edit role name, description and status.", "roles", false, false),
        new(304, PermissionKeys.Roles.Delete, "Delete Role", "Delete roles that are safe to remove.", "roles", false, false),
        new(305, PermissionKeys.Roles.ManagePermissions, "Manage Role Permissions", "Change the default permissions of a role.", "roles", false, false),
        new(306, PermissionKeys.Roles.ViewAudit, "View Role History", "See the audit history of a role.", "roles", false, true),

        // Projects (not implemented yet)
        new(400, "projects.access", "Tab Access", "Show the Projects tab.", "projects", true, true),
        new(401, "projects.view", "View Projects", "View projects.", "projects", false, true),
        new(402, "projects.create", "Create Project", "Create projects.", "projects", false, true),
        new(403, "projects.edit", "Edit Project", "Edit projects.", "projects", false, true),
        new(404, "projects.delete", "Delete Project", "Delete projects.", "projects", false, true),

        // Tasks (not implemented yet)
        new(500, "tasks.access", "Tab Access", "Show the Tasks tab.", "tasks", true, true),
        new(501, "tasks.view", "View Tasks", "View tasks.", "tasks", false, true),
        new(502, "tasks.create", "Create Task", "Create tasks.", "tasks", false, true),
        new(503, "tasks.edit", "Edit Task", "Edit tasks.", "tasks", false, true),
        new(504, "tasks.delete", "Delete Task", "Delete tasks.", "tasks", false, true),
        new(505, "tasks.assign", "Assign Task", "Assign tasks to members.", "tasks", false, true),
        new(506, "tasks.change_status", "Change Task Status", "Move tasks between statuses.", "tasks", false, true),
        new(507, "tasks.change_deadline", "Change Task Deadline", "Change task due dates.", "tasks", false, true),

        // Meetings (not implemented yet)
        new(600, "meetings.access", "Tab Access", "Show the Meetings tab.", "meetings", true, true),
        new(601, "meetings.view", "View Meetings", "View meeting notes.", "meetings", false, true),
        new(602, "meetings.create", "Create Meeting", "Create meeting notes.", "meetings", false, true),
        new(603, "meetings.edit", "Edit Meeting", "Edit meeting notes.", "meetings", false, true),
        new(604, "meetings.delete", "Delete Meeting", "Delete meeting notes.", "meetings", false, true),

        // Milestones (not implemented yet)
        new(700, "milestones.access", "Tab Access", "Show the Milestones tab.", "milestones", true, true),
        new(701, "milestones.view", "View Milestones", "View milestones.", "milestones", false, true),
        new(702, "milestones.create", "Create Milestone", "Create milestones.", "milestones", false, true),
        new(703, "milestones.edit", "Edit Milestone", "Edit milestones.", "milestones", false, true),
        new(704, "milestones.delete", "Delete Milestone", "Delete milestones.", "milestones", false, true),

        // Global Settings (not implemented yet)
        new(800, "settings.access", "Tab Access", "Show the Global Settings tab.", "settings", true, false),
        new(801, "settings.view", "View Settings", "View application settings.", "settings", false, false),
        new(802, "settings.edit", "Edit Settings", "Change application settings.", "settings", false, false),
    ];

    public static ModuleDefinition Module(string key) => Modules.Single(m => m.Key == key);

    /// <summary>Seed rows for the Permissions table, derived from the definitions above.</summary>
    public static IEnumerable<Permission> ToEntities()
    {
        var modules = Modules.ToDictionary(m => m.Key);
        var tabAccess = Definitions.Where(d => d.IsTabAccess).ToDictionary(d => d.ModuleKey, d => d.Id);
        return Definitions.Select((d, index) => new Permission
        {
            Id = d.Id,
            Key = d.Key,
            DisplayName = d.DisplayName,
            Description = d.Description,
            ModuleKey = d.ModuleKey,
            Category = modules[d.ModuleKey].Name,
            ParentPermissionId = d.IsTabAccess ? null : tabAccess[d.ModuleKey],
            IsActive = modules[d.ModuleKey].IsImplemented,
            AllowMemberOverride = d.AllowMemberOverride,
            SortOrder = index,
            CreatedAtUtc = SeededAtUtc,
        });
    }
}
