namespace HvacConnect.Core.Domain.Enums;

public enum UserRole
{
    SuperAdmin,
    VendorAdmin,
    ServiceCoordinator,
    Technician,
    EndUser
}

public enum VendorStatus
{
    PendingOnboarding,
    Active,
    Inactive
}

public enum OnboardingPhase
{
    Discovery,
    Phase1_Coexist,
    Phase2_Planning,
    Phase2_Cutover,
    Completed
}

public enum CustomerStatus
{
    Active,
    Review,
    Inactive
}

public enum CustomerType
{
    Commercial,
    Retail,
    Industrial,
    Healthcare,
    Hospitality,
    Supermarket,
    Other
}

public enum AssetCategory
{
    RooftopUnit,
    Chiller,
    Boiler,
    WalkInCoolerFreezer,
    RefrigerationRack
}

public enum AssetStatus
{
    Active,
    ServiceReview,
    Decommissioned
}

public enum TicketPriority
{
    Critical,
    High,
    Medium,
    Low
}

public enum TicketStatus
{
    Open,
    Assigned,
    InProgress,
    Resolved,
    Closed
}

public enum SLAState
{
    WithinSLA,
    AtRisk,
    Breached
}

public enum CommentType
{
    UserComment,
    StatusChange,
    Assignment,
    Resolution
}

public enum ImportJobStatus
{
    Uploaded,
    Mapped,
    Validated,
    DuplicateReview,
    Committed,
    Failed
}

public enum ValidationSeverity
{
    Warning,
    Error
}

public enum DuplicateConfidenceLevel
{
    High,
    Medium
}

public enum DuplicateDecision
{
    PendingReview,
    LinkedToCustomer,
    KeepSeparate,
    Skipped,
    Deferred
}

public enum TechnicianAvailability
{
    Available,
    OnJob,
    OffDuty
}

