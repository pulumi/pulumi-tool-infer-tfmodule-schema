using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using CliWrap;
using CliWrap.Buffered;
using static System.Console;
using System.Text.Json.Nodes;

var openAIKey = Environment.GetEnvironmentVariable("OPENAI_KEY");

if (string.IsNullOrEmpty(openAIKey))
{
    WriteLine("Please set the OPENAI_KEY environment variable.");
    return;
}

if (args.Length != 3 && args.Length != 2)
{
    WriteLine("Usage: infer-tfmodule-schema <module_source> [<module_version>] <output_file_name>");
    return;
}

var moduleSource = args[0];
var moduleVersion = args.Length == 3 ? args[1] : "";
var outputFileName = args[args.Length - 1];
var cwd = Directory.GetCurrentDirectory();
var outputFilePath = Path.Combine(cwd, outputFileName);

void WriteEmptyConfig() => File.WriteAllText(outputFilePath, "{ \"outputs\": {} }");

// Returns a list of the paths of the Terraform files in the module
// If the module is local, it will return the paths of the files in the local module directory
// If the module is remote, it will clone the module from GitHub and return the paths of the files in the cloned directory
async Task<List<string>> TerraformFiles()
{
    if (args.Length == 2)
    {
        var localModulePath = Path.Combine(cwd, moduleSource);
        if (!Directory.Exists(localModulePath))
        {
            WriteLine($"Directory {moduleSource} does not exist.");
            return new List<string>();
        }

        var tfFile = Directory.GetFiles(localModulePath, "*.tf", SearchOption.AllDirectories);
        return tfFile.ToList();
    }

    var httpClient = new HttpClient();
    var moduleMetadataUrl = $"https://registry.terraform.io/v1/modules/{moduleSource}/{moduleVersion}";
    WriteLine($"Fetching module metadata from {moduleMetadataUrl}");
    var moduleMetadataResponse = await httpClient.GetAsync(moduleMetadataUrl);
    if (!moduleMetadataResponse.IsSuccessStatusCode)
    {
        WriteLine($"Failed to fetch module metadata: {moduleMetadataResponse.StatusCode}");
        return new List<string>();
    }

    var moduleMetadataContent = await moduleMetadataResponse.Content.ReadAsStringAsync();
    var moduleMetadata = JsonDocument.Parse(moduleMetadataContent);
    var githubSource = moduleMetadata.RootElement.GetProperty("source").GetString();
    if (string.IsNullOrEmpty(githubSource))
    {
        WriteLine("No GitHub source found in module metadata.");
        return new List<string>();
    }

    var description = moduleMetadata.RootElement.GetProperty("description").GetString();

    if (!string.IsNullOrEmpty(description))
    {
        WriteLine($"Module {moduleSource} at {moduleVersion}:");
        WriteLine($"Description: {description}");
    }

    if (!githubSource.EndsWith(".git"))
    {
        githubSource = githubSource + ".git";
    }

    WriteLine($"Cloning {githubSource} to extract source code...");

    var tempDir = Directory.CreateTempSubdirectory("terraform-module");
    var gitCloneCommand = Cli.Wrap("git")
        .WithArguments(["clone", "--depth", "1", githubSource, tempDir.FullName])
        .WithValidation(CommandResultValidation.None);

    var gitCloneResult = await gitCloneCommand.ExecuteBufferedAsync();

    if (gitCloneResult.ExitCode != 0)
    {
        WriteLine($"Failed to clone repository: {gitCloneResult.StandardError}");
        return new List<string>();
    }

    var terraformFiles = Directory.GetFiles(tempDir.FullName, "*.tf", SearchOption.AllDirectories);
    return terraformFiles.ToList();
}


var terraformFiles = await TerraformFiles();
if (terraformFiles.Count == 0)
{
    WriteLine("No Terraform files found to analyze");
    WriteEmptyConfig();
    return;
}

WriteLine($"Found {terraformFiles.Count} Terraform files in the module.");
WriteLine("Reading Terraform files...");

var client = new OpenAIClient(apiKey: openAIKey);
var chat = client.GetChatClient(model: "gpt-4o");

var systemMessage = @"
You are a Terraform module expert. 
You will be given the source code of a terraform module in the form of file paths and their contents
and you will analyze the types of the outputs of that module. The types are usually in a file called outputs.tf
but you should use the whole module to infer the types of the outputs.
";

var userMessage = @"
Please analyze the source code of the following Terraform module and provide the types of the outputs.
The module source code is as follows:
";

List<ChatMessage> messages =
[
    new SystemChatMessage(systemMessage),
    new UserChatMessage(userMessage)
];

foreach (var path in terraformFiles)
{
    string content = File.ReadAllText(path);
    messages.Add(new UserChatMessage($"File: {path}\n{content}"));
}

ChatCompletionOptions options = new()
{
    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: "output_schema",
        jsonSchema: BinaryData.FromBytes("""
            {
                "type": "object",
                "properties": {
                    "outputs": {
                        "type": "array",
                        "items": {
                        "type": "object",
                        "properties": {
                            "output_name": { 
                                "type": "string",
                                "description": "The name of the output variable."
                            },
                            "output_type": {
                                "type": "string",
                                "description": "The type of the output variable which can be one of [string, number, bool, list(string), list(any), map(string), map(any), any, unknown]"
                            }
                        },
                        "required": ["output_name", "output_type"],
                        "additionalProperties": false
                        }
                    }
                },
                "required": ["outputs"],
                "additionalProperties": false
            }
            """u8.ToArray()),
        jsonSchemaIsStrict: true)
};

ChatCompletion completion = chat.CompleteChat(messages, options);

if (completion.Content.Count == 0)
{
    WriteLine("No completion received from model.");
    WriteEmptyConfig();
    return;
}

using JsonDocument structuredJson = JsonDocument.Parse(completion.Content[0].Text);
var inferredOutputs = structuredJson.RootElement.GetProperty("outputs").EnumerateArray().ToList();
WriteLine($"There are {inferredOutputs.Count} output(s)");

var outputs = new JsonObject();
foreach (JsonElement stepElement in inferredOutputs)
{
    var outputName = stepElement.GetProperty("output_name").GetString() ?? "";
    var outputType = stepElement.GetProperty("output_type").GetString() ?? "";
    var pulumiAnyType = new JsonObject { ["$ref"] = "pulumi.json#/Any" };
    outputs[outputName] = outputType switch 
    {
        "string" => new JsonObject { ["type"] = "string" },
        "number" => new JsonObject { ["type"] = "number" },
        "bool" => new JsonObject { ["type"] = "boolean" },
        "list(string)" => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        "list(any)" => new JsonObject { ["type"] = "array", ["items"] = pulumiAnyType },
        "map(string)" => new JsonObject { ["type"] = "object", ["additionalProperties"] = new JsonObject { ["type"] = "string" } },
        "map(any)" => new JsonObject { ["type"] = "object", ["additionalProperties"] = pulumiAnyType },
        _ => pulumiAnyType,
    };
}

var outputSchema = new JsonObject
{
    ["outputs"] = outputs
};

WriteLine($"Writing output to {outputFilePath}");
File.WriteAllText(outputFilePath, outputSchema.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true
}));
