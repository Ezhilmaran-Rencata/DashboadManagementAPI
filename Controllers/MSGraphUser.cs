namespace SandstarAzureAD.Controllers
{
    public class MSGraphUser
    {
        public string Id { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Department { get; set; }
        public DateTimeOffset? DateOfBirth { get; set; }
        public string ManagerId { get; set; }
        public string ReportingManagerName { get; set; }

        public DateTimeOffset? DOJ { get; set; }
        public string ReportingManagerEmail { get; set; }
        public string UserPrincipalName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public string Designation { get; set; }
        public string Location { get; set; }
        public string MobileNumber { get; set; }
        public string OfficeLocation { get; set; }
        public string Team { get; set; }
        public string BloodGroup { get; set; }
        public bool IsActive { get; set; }
    }

    public class MSGraphManager
    {
        public string Id { get; set; }    
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; } 
        public string Designation { get; set; }
    }
}