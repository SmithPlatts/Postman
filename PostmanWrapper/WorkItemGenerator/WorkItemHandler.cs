using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi;
using Postman.Common;
using Microsoft.VisualStudio.Services.Client;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Postman.Wrapper;
using System.IO;

/// <summary>
/// Automatic creation and update of ADO work items of type Test Case, wrapping Postman test cases into MSTest based test cases.
/// </summary>
public class WorkItemHandler
{
    private const string BaseConfigurationFileName = "AzureDevOps.xml";
    private const string ConfigurationFileNamePattern = "AzureDevOps.*";
    private const string ConfigurationFolderNamePattern = "Config*";
    private const string ConnectionUrlVariableName = "Connection_Url";
    private const string ConnectionProjectVariableName = "Connection_Project";
    private const string TestCaseAreaPathVariableName = "TestCase_AreaPath";

    readonly Setup setup;
    readonly ProjectConfiguration configuration;
    readonly VssConnection connection;
    readonly WorkItemTrackingHttpClient witClient;

    readonly string[] _supportedConfigurationFileExtensions = new[] { ".json", ".xml" };

    public WorkItemHandler()
    {
        setup = new Setup();
        configuration = GetConfiguration();
        Console.WriteLine("[INFO] Configuration:");
        Console.WriteLine(configuration.ToString(4));

        if (setup.IsTestAgentRun)
        {
            string accessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
            Assert.IsTrue(!string.IsNullOrEmpty(accessToken), "Cannot retrieve access token in pipeline");
            VssBasicCredential credentials = new VssBasicCredential("", accessToken);
            connection = new VssConnection(configuration.Connection.Url, credentials);
        }
        else
        {
            VssClientCredentials credentials = new VssClientCredentials();
            connection = new VssConnection(configuration.Connection.Url, credentials);
        }
        witClient = connection.GetClient<WorkItemTrackingHttpClient>();
    }

    public void UpdateProjectTestCases()
    {
        Console.WriteLine($"[INFO] System.WorkFolder directory: {setup.SystemWorkFolder}");
        foreach (string path in setup.GetCollectionFilePaths())
        {
            Console.WriteLine($"[INFO] Scoped collection file: {path}");
        }

        // Retrieve list of postman test cases in Wrapper project
        List<MethodInfo> testMethods = GetTestMethods(Assembly.GetAssembly(typeof(PostmanWrapper)));

        // Create/update postman test cases in ADO project
        foreach (MethodInfo mi in testMethods)
        {
            int workItemId = -1;

            foreach (CustomAttributeData cad in  mi.CustomAttributes)
            {
                if (cad.ConstructorArguments.Count == 2 && cad.ConstructorArguments[0].Value.ToString() == "AdoId")
                {
                    workItemId = int.Parse(cad.ConstructorArguments[1].Value.ToString());
                    break;
                }
            }

            if (workItemId > 0)
            {
                Wiql query = new Wiql()
                {
                    Query = string.Format("SELECT [Id] FROM workitems WHERE [System.TeamProject] = '{0}' AND [System.WorkItemType] = '{1}' AND [System.Id] = '{2}'",
                    configuration.Connection.Project, ADOTestCaseWorkItemType(), workItemId)
                };
                WorkItemQueryResult result = witClient.QueryByWiqlAsync(query, configuration.Connection.Project).Result;
                if (result.WorkItems.Count() == 0) throw new Exception(string.Format("Linked Test Case with prescribed id {0} could not be found",workItemId));

                WorkItemReference wir = result.WorkItems.First();
                WorkItem wi = witClient.GetWorkItemAsync(workItemId).Result;
                JsonPatchDocument patchDoc = GetPatchDocumentAutomation(mi, wi);
                if (patchDoc.Count > 0)
                {
                    Task<WorkItem> item = witClient.UpdateWorkItemAsync(patchDoc, configuration.Connection.Project, wir.Id);
                    item.GetAwaiter().GetResult();
                    Console.WriteLine("Update manually linked Test Case : " + workItemId);
                }
                else
                {
                    Console.WriteLine("Manually linked Test Case already up to date : " + workItemId);
                }
            }
            else
            {
                Wiql query = new Wiql() 
                { 
                    Query = string.Format("SELECT [Id] FROM workitems WHERE [System.TeamProject] = '{0}' AND [System.WorkItemType] = '{1}' AND [System.Title] = '{2}' AND [Microsoft.VSTS.TCM.AutomatedTestType] =  '{3}'", 
                    configuration.Connection.Project, ADOTestCaseWorkItemType(), ADOTestCaseTitle(mi), ADOTestCaseAutomatedTestType()) 
                };
                WorkItemQueryResult result = witClient.QueryByWiqlAsync(query, configuration.Connection.Project).Result;
                if (result.WorkItems.Count() == 0)
                {
                    JsonPatchDocument patchDoc = GetPatchDocumentFull(mi, null);
                    Task<WorkItem> item = witClient.CreateWorkItemAsync(patchDoc, configuration.Connection.Project, ADOTestCaseWorkItemType());
                    var res = item.GetAwaiter().GetResult();
                    workItemId = (int)res.Id;
                    Console.WriteLine("Create automatically linked Test Case : " + workItemId);
                }
                else if (result.WorkItems.Count() == 1)
                {
                    WorkItemReference wir = result.WorkItems.First();
                    workItemId = wir.Id;
                    WorkItem wi = witClient.GetWorkItemAsync(workItemId).Result;
                    JsonPatchDocument patchDoc = GetPatchDocumentFull(mi, wi);
                    if (patchDoc.Count > 0)
                    {
                        Task<WorkItem> item = witClient.UpdateWorkItemAsync(patchDoc, configuration.Connection.Project, wir.Id);
                        item.GetAwaiter().GetResult();
                        Console.WriteLine("Update automatically linked Test Case : " + workItemId);
                    }
                    else
                    {
                        Console.WriteLine("Automatically linked Test Case already up to date : " + workItemId);
                    }
                }
                else
                {
                    // For now, we ignore multiple instances of the same test case representation in ADO. 
                    // Most likely multiple instances exist in ADO because the test case has been copied.
                }
            }
        }
    }

    private ProjectConfiguration GetConfiguration()
    {
        FileInfo fallbackConfigurationFile = new FileInfo(BaseConfigurationFileName);

        DirectoryInfo rootDirectory = new DirectoryInfo(setup.GitRootFolder ?? setup.GetGitRootFolder(fallbackConfigurationFile.DirectoryName));
        Console.WriteLine($"[INFO] Configuration root directory: {rootDirectory.FullName}");
        FileInfo configurationFile = GetTopMostConfigurationFile(rootDirectory) ?? fallbackConfigurationFile;
        Console.WriteLine($"[INFO] Configuration file: {configurationFile.FullName}");

        ProjectConfiguration projectConfiguration;
        using (StreamReader reader = configurationFile.OpenText())
        {
            if (configurationFile?.Extension?.Equals(".json", StringComparison.OrdinalIgnoreCase) == true)
            {
                projectConfiguration = ProjectConfiguration.DeserializeJson(reader);
            }
            else
            {
                projectConfiguration = ProjectConfiguration.DeserializeXml(reader);
            }
        }

        string connectionUrlFromVariable = Environment.GetEnvironmentVariable(ConnectionUrlVariableName);
        if (!string.IsNullOrWhiteSpace(connectionUrlFromVariable))
        {
            projectConfiguration.Connection.Url = new Uri(connectionUrlFromVariable);
        }
        string connectionProjectFromVariable = Environment.GetEnvironmentVariable(ConnectionProjectVariableName);
        if (!string.IsNullOrWhiteSpace(connectionProjectFromVariable))
        {
            projectConfiguration.Connection.Project = connectionProjectFromVariable;
        }
        string testCaseAreaPathFromVariable = Environment.GetEnvironmentVariable(TestCaseAreaPathVariableName);
        if (!string.IsNullOrWhiteSpace(testCaseAreaPathFromVariable))
        {
            projectConfiguration.TestCase.AreaPath = testCaseAreaPathFromVariable;
        }

        return projectConfiguration;
    }

    private FileInfo GetTopMostConfigurationFile(DirectoryInfo rootDirectory)
    {
        IEnumerable<FileInfo> getConfigurationFiles(DirectoryInfo directory, string searchPattern, string[] supportedFileExtensions) => directory
            .GetFiles(searchPattern, SearchOption.TopDirectoryOnly)
            .Where(fileInfo => supportedFileExtensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase));
        FileInfo getLastWrittenFile(IEnumerable<FileInfo> fileInfos) => fileInfos?.OrderByDescending(fileInfo => fileInfo.LastWriteTimeUtc).FirstOrDefault();

        IEnumerable<FileInfo> files = getConfigurationFiles(rootDirectory, ConfigurationFileNamePattern, _supportedConfigurationFileExtensions);
        if (files.Any())
        {
            return getLastWrittenFile(files);
        }

        DirectoryInfo[] childDirectories = rootDirectory.GetDirectories(ConfigurationFolderNamePattern, SearchOption.TopDirectoryOnly);
        foreach (DirectoryInfo directory in childDirectories)
        {
            files = getConfigurationFiles(directory, ConfigurationFileNamePattern, _supportedConfigurationFileExtensions);
            if (files.Any())
            {
                break;
            }
        }

        return getLastWrittenFile(files);
    }

    private JsonPatchDocument GetPatchDocumentFull(MethodInfo mi, WorkItem wi)
    {
        JsonPatchDocument patchDocument = GetPatchDocumentAutomation(mi, wi);
        AddJsonPatchOperation(patchDocument, "System.Title", ADOTestCaseTitle(mi), wi);
        AddJsonPatchOperation(patchDocument, "System.Description", ADOTestCaseDescription(), wi);
        AddJsonPatchOperation(patchDocument, "System.AreaPath", ADOTestCaseAreaPath(), wi);
        foreach (var customField in configuration.TestCase.CustomFields) AddJsonPatchOperation(patchDocument, customField.Id, customField.DefaultValue, wi);
        return patchDocument;
    }

    private JsonPatchDocument GetPatchDocumentAutomation(MethodInfo mi, WorkItem wi)
    {
        JsonPatchDocument patchDocument = new JsonPatchDocument();
        AddJsonPatchOperation(patchDocument, "Microsoft.VSTS.TCM.AutomatedTestName", ADOTestCaseAutomatedTestName(mi), wi);
        AddJsonPatchOperation(patchDocument, "Microsoft.VSTS.TCM.AutomatedTestStorage", ADOTestCaseAutomatedTestStorage(mi), wi);
        AddJsonPatchOperation(patchDocument, "Microsoft.VSTS.TCM.AutomatedTestType", ADOTestCaseAutomatedTestType(), wi);
        AddJsonPatchOperation(patchDocument, "Microsoft.VSTS.TCM.AutomatedTestId", GetGuid(), wi, false);
        return patchDocument;
    }

    private void AddJsonPatchOperation(JsonPatchDocument patchDocument,  string fieldName, string value, WorkItem existingWorkItem, bool checkEquality = true)
    {
        if (existingWorkItem == null || !existingWorkItem.Fields.ContainsKey(fieldName) || (checkEquality && existingWorkItem.Fields[fieldName].ToString() != value))
        {
            patchDocument.Add(
                new JsonPatchOperation()
                {
                    Operation = Operation.Add,
                    Path = string.Format("/fields/{0}", fieldName),
                    Value = value
                }
            );
        }

    }

    private string ADOTestCaseWorkItemType()
    {
        return "Test Case";
    }

    private string ADOTestCaseTitle(MethodInfo mi)
    {
        return string.Format("{0} - {1}", mi.ReflectedType.Name, mi.Name);
    }

    private string ADOTestCaseAreaPath()
    {
        return configuration.TestCase.AreaPath;
    }

    private string ADOTestCaseDescription()
    {
        return "Autogenerated wrapper for postman test case.";
    }

    private string ADOTestCaseTestCaseTestType()
    {
        return "Functional";
    }

    private string ADOTestCaseTestCaseAutoStatus()
    {
        return "Automated";
    }

    private string ADOTestCaseTestCaseAutomationStatus()
    {
        return "Automated";
    }

    private string ADOTestCaseAutomatedTestName(MethodInfo mi)
    {
        return string.Format("{0}.{1}", mi.ReflectedType.FullName,mi.Name);
    }

    private string ADOTestCaseAutomatedTestStorage(MethodInfo mi)
    {
        return mi.Module.Name;
    }

    private string ADOTestCaseAutomatedTestType()
    {
        return "Postman Test Case";
    }

    private string GetGuid()
    {
        return Guid.NewGuid().ToString();
    }
    private List<MethodInfo> GetTestMethods(Assembly assembly)
    {
        var methods = assembly.GetTypes()
                              .SelectMany(t => t.GetMethods())
                              .Where(m => m.GetCustomAttributes(typeof(TestMethodAttribute), false).Length > 0)
                              .ToList<MethodInfo>();
        return methods;
    }
}
