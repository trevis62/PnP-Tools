﻿using Microsoft.IdentityModel.Clients.ActiveDirectory;
using PSSQT.Helpers;
using PSSQT.Helpers.Authentication;
using SearchQueryTool.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

/**
 * <ParameterSetName	P1	P2
 * Site                 X   X
 * Query                X   X 
 * LoadPreset	        	X
 **/

namespace PSSQT
{
    public abstract class AbstractSearchSPCmdlet<TSearchRequest>
         : PSCmdlet where TSearchRequest : SearchRequest, new()
    {
        #region PrivateMembers


        private static readonly Dictionary<Guid, CookieCollection> Tokens = new Dictionary<Guid, CookieCollection>();   // SPO Auth tokens

        private static bool SkipSSLValidation;

        private const string separator = "==================================================================================================================";

        protected IProgress progress;

        #endregion

        #region ScriptParameters


        [Parameter(
            Mandatory = true,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            Position = 0,
            HelpMessage = "Query terms.",
            ParameterSetName = "P1"
        )]
        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            Position = 0,
            HelpMessage = "Query terms.",
            ParameterSetName = "P2"
        )]
        public string[] Query { get; set; }

        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Credentials."
        )]
        public PSCredential Credential { get; set; }


        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Accept Type. Accept XML or JSON."
        )]
        public AcceptType? AcceptType { get; set; }

        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Method Type. Use GET or POST."
        )]
        public HttpMethodType? MethodType { get; set; }

        [Parameter(
            Mandatory = true,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "SharePoint site to connect to. If it starts with http(s)//, use directly, otherwise load from connection file. See -SaveSite",
            ParameterSetName = "P1"
        )]
        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "SharePoint site to connect to. If it starts with http(s)//, use directly, otherwise load from connection file. See -SaveSite",
            ParameterSetName = "P2"
        )]
        public string Site { get; set; }


        [Parameter(
            Mandatory = true,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Load parameters from file. Use Search-SPIndex -SavePreset to save a preset. Script parameters on the command line overrides.",
            ParameterSetName = "P2"
        ), ArgumentCompleter(typeof(PresetCompleter))]
        [Alias("Preset")]
        public string LoadPreset { get; set; }


        [Parameter(
            Mandatory = false,
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Specify authentication mode."
        )]


        public PSAuthenticationMethod? AuthenticationMethod { get; set; }  // Environment variable can be used to set default


        [Parameter(
             ValueFromPipelineByPropertyName = false,
             ValueFromPipeline = false,
             HelpMessage = "Force a login prompt when you are using -AuthenticationMode SPOManagement."
         )]
        public SwitchParameter ForceLoginPrompt { get; set; }

        [Parameter(
            ValueFromPipelineByPropertyName = false,
            ValueFromPipeline = false,
            HelpMessage = "Skip validation of SSL certificate."
        )]
        public SwitchParameter SkipServerCertificateValidation { get; set; }

        [Parameter(
             Mandatory = false,
             ValueFromPipelineByPropertyName = false,
             ValueFromPipeline = false,
             HelpMessage = "Select the type of progress indicator. Default is PowerShell WriteProgress."
         )]
        public ProgressType ProgressIndicator { get; set; } = ProgressType.Default;
        #endregion

        #region Methods


        protected AbstractSearchSPCmdlet()
        {
            ServicePointManager.ServerCertificateValidationCallback +=
                new RemoteCertificateValidationCallback(ValidateServerCertificate);
        }

        protected bool UsingPreset
        {
            get
            {
                return ParameterSetName == "P2" && !String.IsNullOrWhiteSpace(LoadPreset);
            }
        }

        protected override void ProcessRecord()
        {
            try
            {
                base.ProcessRecord();

                progress = ProgressFactory.CreateProgressIndicator(ProgressIndicator, this);

                SkipSSLValidation = SkipServerCertificateValidation.IsPresent;

                WriteDebug($"Enter {GetType().Name} ProcessRecord");

                SearchConnection searchConnection = new SearchConnection();
                TSearchRequest searchRequest = new TSearchRequest();

                // Load Preset
                if (ParameterSetName == "P2")
                {
                    SearchPreset preset = LoadPresetFromFile();

                    searchConnection = preset.Connection;

                    PresetLoaded(ref searchRequest, preset);
                }

                // additional command line argument validation. Throw an error if not valid
                ValidateCommandlineArguments();

                // Set Script Parameters from Command Line. Override in deriving classes

                SetRequestParameters(searchRequest);

                // Save Site/Preset

                if (IsSavePreset())
                {
                    SaveRequestPreset(searchConnection, searchRequest);
                }
                else
                {
                    EnsureValidQuery(searchRequest);

                    WriteVerboseInformation(searchRequest);

                    ExecuteRequest(searchRequest);
                }
            }
            catch (Exception ex)
            {
                // always write last error to a file with as much detail as possible.
                try
                {
                    WriteErrorDetailsToFile(ex);
                }
                catch (Exception wedEx)
                {
                    WriteWarning($"Failed to write error details to file: {wedEx.Message}");
                    WriteDebug(wedEx.StackTrace);
                }

                WriteError(CreateErrorRecord(ex));
                if (ex.InnerException != null)
                {
                    WriteError(CreateErrorRecord(ex.InnerException));
                }

                WriteDebug(ex.StackTrace);

                WriteWarning($"Error details were written to {GetLastErrorFile()}.");
            }
        }

        protected virtual ErrorRecord CreateErrorRecord(Exception ex)
        {
            return new ErrorRecord(ex, GetErrorId(), GetErrorCategory(ex), null);
        }

        protected virtual ErrorCategory GetErrorCategory(Exception ex)
        {
            if (ex is AdalException)
            {
                return ErrorCategory.AuthenticationError;
            }
            else if (ex.Message.Contains("HTTP 403: Forbidden"))
            {
                return ErrorCategory.PermissionDenied;
            }
            else if (ex.Message.Contains("HTTP 401: Unauthorized"))
            {
                return ErrorCategory.PermissionDenied;
            }
            else if (ex is NotImplementedException)
            {
                return ErrorCategory.NotImplemented;
            }

            return ErrorCategory.NotSpecified;
        }

        private static string GetLastErrorFile()
        {
            return GetLastErrorFile(GetLastErrorDir());
        }

        private static string GetLastErrorFile(string dir)
        {
            return Path.Combine(dir, "LastError.txt");
        }

        private static string GetLastErrorDir()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "PSSQT");

            return dir;
        }

        // if overriding, consider overriding WriteErrorDetailsToFile(Excption, StreamWriter) instead
        protected virtual void WriteErrorDetailsToFile(Exception ex)
        {
            string dir = GetLastErrorDir();

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string outErrorFile = GetLastErrorFile(dir);

            if (File.Exists(outErrorFile))
            {
                var previousErrorFile = Path.Combine(dir, "PreviousError.txt");

                if (File.Exists(previousErrorFile))
                {
                    File.Delete(previousErrorFile);
                }

                File.Move(outErrorFile, previousErrorFile);
            }

            using (StreamWriter file = new StreamWriter(outErrorFile))
            {
                WriteErrorDetailsToFile(ex, file);
            }

        }


        protected virtual void WriteErrorDetailsToFile(Exception ex, StreamWriter file)
        {
            var now = DateTime.Now;

            file.WriteLine($"{GetType().Name} request failed with an exception: {ex.Message}");
            file.WriteLine();
            file.WriteLine($"Time: {now}, UTC Time: {now.ToUniversalTime()}");
            file.WriteLine();
            file.WriteLine(separator);
            file.WriteLine();

            file.WriteLine($"Source: {ex.Source}");
            file.WriteLine($"Target Site: {ex.TargetSite}");
            file.WriteLine($"HResult: {ex.HResult}");
            file.WriteLine($"Help Link: {ex.HelpLink}");
            file.WriteLine();

            if (ex.Data != null)
            {
                var keys = ex.Data.Keys;

                if (keys.Count > 0)
                {
                    file.WriteLine("Exception Data:");
                    file.WriteLine(separator);
                    foreach (var key in keys)
                    {
                        file.WriteLine($"Data: {key} => {ex.Data[key]}");
                    }
                }
            }

            file.WriteLine(separator);
            file.WriteLine("EXCEPTION SUMMARY");
            file.WriteLine(separator);

            try
            {
                WriteExceptionInfo(ex, file);
            }
            catch (Exception weiEx)
            {
                WriteWarning($"Failed to write exception info to file: {weiEx.Message}");
            }

            file.WriteLine();
            file.WriteLine(separator);
            file.WriteLine("EXCEPTION DETAIL");
            file.WriteLine(separator);
            file.WriteLine();

            file.WriteLine($"Exception detail: {ex.ToString()}");

            file.WriteLine();
            file.WriteLine(separator);


        }

        private void WriteExceptionInfo(Exception exception, StreamWriter file, int level = 1)
        {
            file.WriteLine();
            file.WriteLine($"[{level}] Exception: {exception.GetType().Name}: {exception.Message}");

            var stList = exception.StackTrace?.ToString().Split('\\');

            file.WriteLine($"Exception occurred at {stList?.Last() ?? "<unknown>"}");

            if (exception.InnerException != null)
            {
                WriteExceptionInfo(exception.InnerException, file, level + 1);
            }
        }

        protected virtual void WriteVerboseInformation(TSearchRequest searchRequest)
        {
            WriteVerbose($"Using authentication method {Enum.GetName(typeof(AuthenticationType), searchRequest.AuthenticationType)}");
        }

        protected virtual void ValidateCommandlineArguments()
        {
            return;       // override if necessary
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            if (!SkipSSLValidation)
            {
                Console.WriteLine($"Certificate error: {sslPolicyErrors}");
            }

            // Do not allow this client to communicate with unauthenticated servers unless Skip validation is set.
            return SkipSSLValidation;
        }

        protected abstract void SaveRequestPreset(SearchConnection searchConnection, TSearchRequest searchRequest);

        protected abstract bool IsSavePreset();

        protected abstract void PresetLoaded(ref TSearchRequest searchRequest, SearchPreset preset);

        protected abstract void ExecuteRequest(TSearchRequest searchRequest);

        protected virtual string GetErrorId()
        {
            return GetType().Name;
        }

        protected SearchPreset LoadPresetFromFile()
        {
            string path = GetPresetFilename(LoadPreset, true);

            WriteVerbose("Loading preset " + path);

            return new SearchPreset(path, true);
        }

        protected virtual void EnsureValidQuery(TSearchRequest searchRequest)
        {
            if (String.IsNullOrWhiteSpace(searchRequest.QueryText))
            {
                throw new Exception("Query text cannot be null.");
            }
        }

        protected virtual void SetRequestParameters(TSearchRequest searchRequest)
        {
            searchRequest.SharePointSiteUrl = GetSPSite() ?? searchRequest.SharePointSiteUrl;
            //searchConnection.SpSiteUrl = searchRequest.SharePointSiteUrl;

            searchRequest.QueryText = GetQuery() ?? searchRequest.QueryText;

            searchRequest.HttpMethodType = MethodType.HasValue ? MethodType.Value : searchRequest.HttpMethodType;
            //searchConnection.HttpMethod = searchRequest.HttpMethodType.ToString();

            searchRequest.AcceptType = AcceptType.HasValue ? AcceptType.Value : searchRequest.AcceptType;

            SetRequestAutheticationType(searchRequest);
        }

        protected virtual void SetRequestAutheticationType(SearchRequest searchRequest)
        {
            if (AuthenticationMethod != null)       // User specified AuthenticationMethod on command line. Always use that. Overrides preset
            {
                LoginBasedOnAuthenticationMethod(searchRequest);

            }
            else if (UsingPreset)                   // AuthenticationMethod == null, use value from preset file 
            {
                LoginBasedOnSearchRequestAuthenticationType(searchRequest);

            }
            else // No AthenticationMethod specified and no preset used
            {
                // Use default method set in environment or if not set, let's try to guess based on the site URL. Does hostname end with sharepoint.com? Yes, then assume SPOManagement
                AuthenticationMethod = PSAuthenticationMethodFactory.DefaultAutenticationMethod() ?? GuessAuthenticationMethod(searchRequest) ?? PSAuthenticationMethod.CurrentUser;

                //WriteVerbose($"Using authentication method {Enum.GetName(typeof(PSAuthenticationMethod), AuthenticationMethod)}");

                LoginBasedOnAuthenticationMethod(searchRequest);
            }
        }

        private void LoginBasedOnSearchRequestAuthenticationType(SearchRequest searchRequest)
        {
            switch (searchRequest.AuthenticationType)
            {
                case AuthenticationType.CurrentUser:
                    CurrentUserLogin(searchRequest);
                    break;
                case AuthenticationType.Windows:
                    WindowsLogin(searchRequest);
                    break;
                case AuthenticationType.SPO:
                    SPOLegacyLogin(searchRequest);
                    break;
                case AuthenticationType.SPOManagement:
                    SPOManagementLogin(searchRequest);
                    break;

                case AuthenticationType.Anonymous:
                case AuthenticationType.Forefront:
                case AuthenticationType.Forms:
                default:
                    throw new NotImplementedException($"PSSQT does not support AuthenticationType {Enum.GetName(typeof(AuthenticationType), searchRequest.AuthenticationType)}. You can override on the command line.");
            }
        }

        private void LoginBasedOnAuthenticationMethod(SearchRequest searchRequest)
        {
            switch (AuthenticationMethod)
            {
                case PSAuthenticationMethod.CurrentUser:
                    CurrentUserLogin(searchRequest);
                    break;
                case PSAuthenticationMethod.Windows:
                    WindowsLogin(searchRequest);
                    break;
                case PSAuthenticationMethod.SPO:
                    SPOLegacyLogin(searchRequest);
                    break;
                case PSAuthenticationMethod.SPOManagement:
                    SPOManagementLogin(searchRequest);
                    break;
                default:
                    throw new NotImplementedException($"Unsupported PSAuthenticationMethod {Enum.GetName(typeof(PSAuthenticationMethod), AuthenticationMethod)}");
            }
        }

        protected virtual void SPOManagementLogin(SearchRequest searchRequest)
        {
            if (Credential != null)
            {
                AdalLogin(new AdalUserCredentialAuthentication(new UserPasswordCredential(Credential.UserName, Credential.Password)), searchRequest, ForceLoginPrompt.IsPresent);
            }
            else
            {
                AdalLogin(searchRequest, ForceLoginPrompt.IsPresent);
            }
        }

        protected virtual void CurrentUserLogin(SearchRequest searchRequest)
        {
            searchRequest.AuthenticationType = AuthenticationType.CurrentUser;
            WindowsIdentity currentWindowsIdentity = WindowsIdentity.GetCurrent();
            searchRequest.UserName = currentWindowsIdentity.Name;
        }

        protected virtual void WindowsLogin(SearchRequest searchRequest)
        {
            if (Credential == null)
            {
                var userName = searchRequest.UserName;

                Credential = this.Host.UI.PromptForCredential("Enter username/password", "", userName, "");
            }

            searchRequest.AuthenticationType = AuthenticationType.Windows;
            searchRequest.UserName = Credential.UserName;
            searchRequest.SecurePassword = Credential.Password;
        }

        internal virtual void SPOLegacyLogin(SearchRequest searchRequest)
        {
            Guid runspaceId = Guid.Empty;
            using (var ps = PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                runspaceId = ps.Runspace.InstanceId;

                CookieCollection cc;

                bool found = Tokens.TryGetValue(runspaceId, out cc);

                if (!found)
                {
                    cc = PSWebAuthentication.GetAuthenticatedCookies(this, searchRequest.SharePointSiteUrl, AuthenticationType.SPO);

                    if (cc == null)
                    {
                        throw new RuntimeException("Authentication cookie returned is null! Authentication failed. Please try again.");  // TODO find another exception
                    }
                    else
                    {
                        Tokens.Add(runspaceId, cc);
                    }
                }

                searchRequest.AuthenticationType = AuthenticationType.SPO;
                searchRequest.Cookies = cc;
                //searchSuggestionsRequest.Cookies = cc;
            }
        }

        protected virtual PSAuthenticationMethod? GuessAuthenticationMethod(SearchRequest searchRequest)
        {
            // AuthenticationMethod == null; User did not specify one

            PSAuthenticationMethod? result = null;


            if (Credential != null)    // SPOManagemnt or Windows
            {
                result = GuessAuthenticationMethodFromHostname(searchRequest, PSAuthenticationMethod.Windows);
            }
            else
            {                           // SPOManagement or CurrentUser
                result = GuessAuthenticationMethodFromHostname(searchRequest, PSAuthenticationMethod.CurrentUser);
            }

            return result;
        }

        private static PSAuthenticationMethod? GuessAuthenticationMethodFromHostname(SearchRequest searchRequest, PSAuthenticationMethod alternateMethod)
        {
            PSAuthenticationMethod? result = null;

            var siteUrl = searchRequest.SharePointSiteUrl;

            if (!String.IsNullOrWhiteSpace(siteUrl))
            {
                if (Uri.TryCreate(siteUrl, UriKind.Absolute, out Uri uri))
                {
                    if (uri.Host.ToLower().EndsWith("sharepoint.com"))
                    {
                        result = PSAuthenticationMethod.SPOManagement;
                    }
                    else
                    {
                        result = alternateMethod;
                    }
                }
            }

            return result;
        }

        internal static void AdalLogin(SearchRequest searchRequest, bool forceLogin)
        {
            AdalLogin(new AdalAuthentication(), searchRequest, forceLogin);
        }

        internal static void AdalLogin(AdalAuthentication adalAuth, SearchRequest searchRequest, bool forceLogin)
        {
            var task = adalAuth.Login(searchRequest.SharePointSiteUrl, forceLogin);

            if (!task.Wait(300000))
            {
                throw new TimeoutException("Prompt for user credentials timed out after 5 minutes.");
            }

            var token = task.Result;

            searchRequest.AuthenticationType = AuthenticationType.SPOManagement;
            searchRequest.Token = token;
        }

        private string GetQuery()
        {
            return Query == null ? null : String.Join(" ", Query);
        }


        protected string GetPresetFilename(string presetName, bool searchPath = false)
        {
            string path = presetName;

            if (!path.EndsWith(".xml"))
            {
                path += ".xml";
            }

            if (searchPath && !Path.IsPathRooted(path))
            {
                // always check current directory first
                var rootedPath = GetRootedPath(path);

                if (!File.Exists(rootedPath))
                {
                    var environmentVariable = Environment.GetEnvironmentVariable("PSSQT_PresetsPath");

                    if (environmentVariable != null)
                    {
                        var result = environmentVariable
                            .Split(';')
                            .Where(s => File.Exists(Path.Combine(s, path)))
                            .FirstOrDefault();

                        if (result == null)
                        {
                            throw new ArgumentException(String.Format("File \"{0}\" not found in current directory or PSSQT_PresetsPath", path));
                        }

                        return Path.Combine(result, path);
                    }
                }

                return rootedPath;
            }


            return GetRootedPath(path);
        }

        internal string GetRootedPath(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, path);
            }

            return path;
        }


        // Hoping that ExclusionSets will be available soon. Would clean this up.
        protected static bool? GetThreeWaySwitchValue(SwitchParameter enable, SwitchParameter disable)
        {
            bool? result = null;

            if (enable) result = true;
            if (disable) result = false;    // disable overrides enable
                                            // else  result = null which means use default value
            return result;
        }


        protected string GetSPSite()
        {
            if (String.IsNullOrWhiteSpace(Site) || Site.StartsWith("http://") || Site.StartsWith("https://"))
            {
                return Site;
            }


            var fileName = GetPresetFilename(Site);

            if (!File.Exists(fileName))
            {
                throw new RuntimeException($"File not found: \"{fileName}\"");
            }

            SearchConnection sc = new SearchConnection();

            sc.Load(fileName);

            if (sc.SpSiteUrl == null)
            {
                throw new ArgumentException($"Unable to load valid saved site information from the file \"{fileName}\"");
            }

            return sc.SpSiteUrl;
        }



        #endregion
    }
}
