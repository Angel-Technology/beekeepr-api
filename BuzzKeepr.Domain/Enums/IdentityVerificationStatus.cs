namespace BuzzKeepr.Domain.Enums;

public enum IdentityVerificationStatus
{
    NotStarted = 0,
    Created = 1,
    Pending = 2,
    Completed = 3,
    NeedsReview = 4,
    Approved = 5,
    Declined = 6,
    Failed = 7,
    Expired = 8
}
