using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using System.Data;
using System.Data.SqlClient;
using System.Net.Http.Headers;

namespace SandstarAzureAD.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AzureADController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<AzureADController> _logger;
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly IConfiguration _configuration;
        public string _clientId = string.Empty;
        public string _clientSecret = string.Empty;
        public string _tenantId = string.Empty;
        public string _securityGroupId = string.Empty;
        public string _baseUrl = string.Empty;

        public IList<MSGraphUser> Users { get; set; }

        public AzureADController(ILogger<AzureADController> logger, IRecurringJobManager recurringJobManager, IConfiguration configuration)
        {
            _logger = logger;
            _recurringJobManager = recurringJobManager;
            _configuration = configuration;
            _clientId = _configuration.GetSection("EntitlementSettings:ClientId").Value.ToString();
            _clientSecret = _configuration.GetSection("EntitlementSettings:ClientSecret").Value.ToString();
            _tenantId = _configuration.GetSection("EntitlementSettings:TenantId").Value.ToString();
            _securityGroupId = _configuration.GetSection("EntitlementSettings:SecurityGroupId").Value.ToString();
            _connectionString = _configuration.GetConnectionString("ManagementDashboardConnection").ToString();
            _baseUrl = _configuration.GetSection("EntitlementSettings:BaseUrl").Value.ToString();
        }

        [HttpGet]
        [Route("IRecurringJob")]
        public string RecurringJobs()
        {
            //_recurringJobManager.AddOrUpdate("jobId", () => GetAllActiveUserToInsertDB(), Cron.Minutely());
            return "Start AuditLogs From SharePoint!";
        }

        [HttpGet]
        [Route("AddorRemoveUsers")]
        public async Task<string> AddorRemoveUsers(string email, bool status)
        {
            try
            {
                string securityGroupId = _securityGroupId;
                // Create the authentication provider
                IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                    .Create(_clientId)
                    .WithClientSecret(_clientSecret)
                    .WithAuthority(new Uri(_baseUrl + _tenantId))
                    .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);

                // Create a GraphServiceClient
                GraphServiceClient graphClient = new GraphServiceClient(authProvider);
                User? userObject = await GetUserIsExistFromAD(email);// graphClient.Users[email].Request().GetAsync().Result; //
                if (userObject != null && userObject.Id != null)
                {
                    var userObjectId = userObject.Id;
                    List<User> groupOfUsers = new List<User>();
                    var groupMembers = graphClient.Groups[securityGroupId].Members.Request().GetAsync().Result;
                    groupOfUsers.AddRange(groupMembers.CurrentPage.OfType<User>());
                    while (groupMembers.NextPageRequest != null)
                    {
                        groupMembers = await groupMembers.NextPageRequest.GetAsync();
                        groupOfUsers.AddRange(groupMembers.CurrentPage.OfType<User>());
                    }
                    var getUserFromGroup = groupOfUsers.Where(x => x.Mail == userObject.Mail).Count();
                    if (status == true)
                    {
                        if (getUserFromGroup == 0)
                        {
                            // Add user to the security group
                            await AddUserToSecurityGroup(graphClient, securityGroupId, userObjectId);
                            UpdateUserBySMDGroup(userObject.Mail);
                            return await Task.FromResult(string.Format("Successfully Added the user {0} to group", userObject.Mail));
                        }
                        else
                        {
                            return await Task.FromResult(string.Format("The {0} user already Added in the group  ", userObject.Mail));
                        }
                    }
                    if (status == false)
                    {
                        if (getUserFromGroup > 0)
                        {
                            // Remove user from the security group
                            await RemoveUserFromSecurityGroup(graphClient, securityGroupId, userObjectId);
                            UpdateUserBySMDGroup(userObject.Mail);
                            return await Task.FromResult(string.Format("Successfully Removed the user {0} from group", userObject.Mail));
                        }
                        else
                        {
                            return await Task.FromResult(string.Format("The {0} User not available in the group to remove", userObject.Mail));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult("User Not Found");
        }

        [NonAction]
        public async Task AddUserToSecurityGroup(GraphServiceClient graphClient, string securityGroupId, string userObjectId)
        {
            try
            {
                await graphClient.Groups[securityGroupId].Members.References.Request().AddAsync(new DirectoryObject
                {
                    Id = userObjectId
                });

                Console.WriteLine("User added to the security group successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding user to the security group: {ex.Message}");
            }
        }


        [NonAction]
        public async Task RemoveUserFromSecurityGroup(GraphServiceClient graphClient, string securityGroupId, string userObjectId)
        {
            try
            {
                await graphClient.Groups[securityGroupId].Members[userObjectId].Reference.Request().DeleteAsync();

                Console.WriteLine("User removed from the security group successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing user from the security group: {ex.Message}");
            }
        }


        [NonAction]

        //[HttpGet]
        //[Route("GetUserIsExistFromAD")]
        public async Task<User> GetUserIsExistFromAD(string email)
        {
            User? user = new User();
            Root root = new Root();
            try
            {
                var client = new HttpClient();
                var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://graph.microsoft.com/v1.0/users?$filter=mail eq '" + email + "'");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAuthorizationToken());
                var response = client.Send(request);
                var result = response.Content.ReadAsStringAsync().Result;
                root = JsonConvert.DeserializeObject<Root>(result);
                user = (root.value != null && root.value.Count > 0) ? root.value[0] : null;
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult(user);
        }

        [HttpGet]
        [Route("GetAllActiveUsersFromAD")]
        public async Task<List<MSGraphUser>> GetAllActiveUsersFromAD()
        {
            List<MSGraphUser> msGraphUsers = new List<MSGraphUser>();
            try
            {
                // Create the authentication provider
                IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                    .Create(_clientId)
                    .WithClientSecret(_clientSecret)
                    .WithAuthority(new Uri(_baseUrl + _tenantId))
                    .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);

                // Create a GraphServiceClient
                GraphServiceClient graphClient = new GraphServiceClient(authProvider);
                var filter = "accountEnabled eq true";
                List<User> allUsers = new List<User>();
                var users = graphClient.Users.Request().Filter(filter).GetAsync().Result;
                allUsers.AddRange(users.CurrentPage.OfType<User>());
                while (users.NextPageRequest != null)
                {
                    users = await users.NextPageRequest.GetAsync();
                    allUsers.AddRange(users.CurrentPage.OfType<User>());
                }

                foreach (var u in allUsers)
                {
                    if (u.Mail != null)
                    {
                        MSGraphUser user = new MSGraphUser();
                        user.Id = u.Id;
                        user.UserPrincipalName = u.UserPrincipalName;
                        user.DisplayName = u.DisplayName;
                        user.Email = u.Mail;
                        user.Department = u.JobTitle;
                        user.OfficeLocation = u.OfficeLocation;
                        user.MobileNumber = u.MobilePhone;
                        if (u.JobTitle != null)
                        {
                            User managerObject = (User)await graphClient.Users[u.UserPrincipalName].Manager.Request().GetAsync();
                            user.ManagerId = managerObject.Id;
                            user.ReportingManagerName = managerObject.DisplayName;
                            user.ReportingManagerEmail = managerObject.Mail;
                        }
                        msGraphUsers.Add(user);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult(msGraphUsers);
        }

        [HttpGet]
        [Route("GetAllUsersFromSMDGroup")]
        public async Task<List<MSGraphUser>> GetAllUsersFromSMDGroup()
        {
            List<MSGraphUser> msGraphGroupOfUsers = new List<MSGraphUser>();
            try
            {
                string securityGroupId = _securityGroupId;
                // Create the authentication provider
                IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                    .Create(_clientId)
                    .WithClientSecret(_clientSecret)
                    .WithAuthority(new Uri(_baseUrl + _tenantId))
                    .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);

                // Create a GraphServiceClient
                GraphServiceClient graphClient = new GraphServiceClient(authProvider);
                List<User> groupOfUsers = new List<User>();
                var groupMembers = graphClient.Groups[securityGroupId].Members.Request().GetAsync().Result;
                groupOfUsers.AddRange(groupMembers.CurrentPage.OfType<User>());
                while (groupMembers.NextPageRequest != null)
                {
                    groupMembers = await groupMembers.NextPageRequest.GetAsync();
                    groupOfUsers.AddRange(groupMembers.CurrentPage.OfType<User>());
                }
                foreach (var u in groupOfUsers)
                {
                    if (u.Mail != null)
                    {
                        MSGraphUser user = new MSGraphUser();
                        user.Id = u.Id;
                        user.UserPrincipalName = u.UserPrincipalName;
                        user.DisplayName = u.DisplayName;
                        user.Email = u.Mail;
                        user.Department = u.JobTitle;
                        user.OfficeLocation = u.OfficeLocation;
                        user.MobileNumber = u.MobilePhone;
                        msGraphGroupOfUsers.Add(user);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult(msGraphGroupOfUsers);
        }

        [HttpGet]
        [Route("GetAllManagersFromAD")]
        public async Task<List<MSGraphUser>> GetAllManagersFromAD()
        {
            List<MSGraphUser> msGraphUsers = new List<MSGraphUser>();
            try
            {
                // Create the authentication provider
                IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                    .Create(_clientId)
                    .WithClientSecret(_clientSecret)
                    .WithAuthority(new Uri(_baseUrl + _tenantId))
                    .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);

                // Create a GraphServiceClient
                GraphServiceClient graphClient = new GraphServiceClient(authProvider);
                var filter = "accountEnabled eq true";
                List<User> allUsers = new List<User>();
                var users = graphClient.Users.Request().Filter(filter).GetAsync().Result;
                allUsers.AddRange(users.CurrentPage.OfType<User>());
                while (users.NextPageRequest != null)
                {
                    users = await users.NextPageRequest.GetAsync();
                    allUsers.AddRange(users.CurrentPage.OfType<User>());
                }

                foreach (var u in allUsers)
                {
                    if (u.Mail != null)
                    {
                        MSGraphUser user = new MSGraphUser();
                        user.Id = u.Id;
                        user.UserPrincipalName = u.UserPrincipalName;
                        user.DisplayName = u.DisplayName;
                        user.Email = u.Mail;
                        user.Department = u.JobTitle;
                        user.OfficeLocation = u.OfficeLocation;
                        user.MobileNumber = u.MobilePhone;
                        if (u.JobTitle != null)
                        {
                            User managerObject = (User)await graphClient.Users[u.UserPrincipalName].Manager.Request().GetAsync();
                            user.ManagerId = managerObject.Id;
                            user.ReportingManagerName = managerObject.DisplayName;
                            user.ReportingManagerEmail = managerObject.Mail;
                        }
                        msGraphUsers.Add(user);
                    }
                }

                var manager = (from u in msGraphUsers
                               select new MSGraphManager()
                               {
                                   Id = u.ManagerId,
                                   EmployeeName = u.ReportingManagerName,
                                   Email = u.ReportingManagerEmail
                               }).Distinct().ToList();
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult(msGraphUsers);
        }

        [HttpGet]
        [Route("GetAllActiveGroupsFromAD")]
        public async Task<string> GetAllActiveGroupsFromAD()
        {
            string content = string.Empty;
            try
            {
                var client = new HttpClient();
                var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://graph.microsoft.com/v1.0/groups");
                request.Headers.Authorization =
                  new AuthenticationHeaderValue("Bearer", GetAuthorizationToken());
                var response = await client.SendAsync(request);
                content = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred...", ex);
            }
            return await Task.FromResult(content);
        }


        [HttpGet]
        [Route("InsertUserFromAzureADToDB")]
        public async Task<List<MSGraphUser>> InsertUserFromAzureADToDB()
        {
            List<MSGraphUser> msGraphUsers = new List<MSGraphUser>();
            try
            {
                List<UserInfoManagement> list = new List<UserInfoManagement>();
                IConfidentialClientApplication app;
                app = ConfidentialClientApplicationBuilder.Create(_clientId)
                        .WithClientSecret(_clientSecret)
                        .WithAuthority(new Uri(_baseUrl + _tenantId))
                        .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(app);
                GraphServiceClient graphClient = new GraphServiceClient(authProvider);



                var filter = "accountEnabled eq true";
                List<User> allUsers = new List<User>();
                List<User> manager = new List<User>();

                var users = graphClient.Users.Request().Filter(filter).GetAsync().Result;
                allUsers.AddRange(users.CurrentPage.OfType<User>());
                while (users.NextPageRequest != null)
                {
                    users = users.NextPageRequest.GetAsync().Result;
                    allUsers.AddRange(users.CurrentPage.OfType<User>());
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    try
                    {
                        conn.Open();
                        using (SqlCommand cmd = new SqlCommand("[SAdmin].[up_GetAuthorizedManager_MgmtApp]", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            SqlDataReader sqlReader = cmd.ExecuteReader();
                            while (sqlReader.Read())
                            {
                                list.Add(new UserInfoManagement
                                {
                                    UserId = Convert.ToInt16(sqlReader["UserId"].ToString()),
                                    EmployeeCode = sqlReader["EmployeeCode"].ToString(),
                                    EmployeeName = sqlReader["EmployeeName"].ToString(),
                                    Email = sqlReader["Email"].ToString(),
                                    Location = sqlReader["Location"].ToString(),
                                    Mobile = sqlReader["Mobile"].ToString(),
                                    ReportingManagerCode = sqlReader["ReportingManagerCode"].ToString(),
                                    ReportingManagerName = sqlReader["ReportingManagerName"].ToString(),
                                    Photo = sqlReader["Photo"].ToString(),
                                    IsLoginEnabled = Convert.ToBoolean(sqlReader["IsLoginEnabled"].ToString()),
                                    AccessLevel = Convert.ToInt16(sqlReader["AccessLevel"].ToString()),
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                    finally
                    {
                        conn.Close();
                    }
                }

                foreach (var u in allUsers)
                {
                    if (u.Mail != null)
                    {
                        MSGraphUser user = new MSGraphUser();
                        user.EmployeeCode = list.Where(x => x.Email == u.Mail).FirstOrDefault().EmployeeCode;
                        user.EmployeeName = u.DisplayName;
                        user.Designation = u.JobTitle;
                        user.Department = u.Department;
                        user.DateOfBirth = u.Birthday;
                        if (u.JobTitle != null)
                        {
                            User managerObject = (User)await graphClient.Users[u.UserPrincipalName].Manager.Request().GetAsync();
                            user.ManagerId = managerObject.Id;
                            user.ReportingManagerName = managerObject.DisplayName;
                            user.ReportingManagerEmail = managerObject.Mail;
                        }
                        user.Id = u.Id;
                        user.UserPrincipalName = u.UserPrincipalName;
                        user.DisplayName = u.DisplayName;
                        user.Email = u.Mail;
                        user.OfficeLocation = u.OfficeLocation;
                        user.MobileNumber = u.MobilePhone;
                        user.Location = u.City;
                        user.DOJ = u.HireDate;
                        user.IsActive = true;
                        msGraphUsers.Add(user);
                    }
                }



            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            return msGraphUsers;
        }

        [NonAction]
        public string UpdateUserBySMDGroup(string email)
        {
            string status = string.Empty;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                try
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("[STimesheet].[up_UpdateUserBySMDGroup_MgmtApp]", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add(new SqlParameter("@Email", email));
                        int temp = Convert.ToInt32(cmd.ExecuteNonQuery());
                        if (temp > 0)
                            status = "Error while updating";
                        else
                            status = "Updated Successfully";

                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message, ex);
                }
                finally
                {
                    conn.Close();
                }
            }
            return status;
        }

        [HttpGet]
        [Route("GetAuthorizationToken")]
        public string GetAuthorizationToken()
        {
            string accesstoken = string.Empty;
            try
            {
                IConfidentialClientApplication confidentialClientApplication = ConfidentialClientApplicationBuilder
                    .Create(_clientId)
                    .WithClientSecret(_clientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
                    .Build();
                ClientCredentialProvider authProvider = new ClientCredentialProvider(confidentialClientApplication);
                string[] scopes = new string[] { "https://graph.microsoft.com/.default" };
                var accessToken = confidentialClientApplication.AcquireTokenForClient(scopes).ExecuteAsync().Result;
                accesstoken = accessToken.AccessToken;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            return accesstoken;
        }
    }
    public class UserInfoManagement
    {
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Email { get; set; }
        public string Designation { get; set; }
        public string Department { get; set; }
        public string Location { get; set; }
        public string Mobile { get; set; }
        public string ReportingManagerCode { get; set; }
        public string ReportingManagerName { get; set; }
        public string ReportingManagerEmail { get; set; }
        public bool IsLoginEnabled { get; set; }
        public int AccessLevel { get; set; }
        public int UserId { get; set; }
        public string Customer { get; set; }
        public string Photo { get; set; }
    }

    public class Root
    {
        [JsonProperty("@odata.context")]
        public string odatacontext { get; set; }
        public List<User> value { get; set; }
    }
}