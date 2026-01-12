using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using static SupportConcierge.SpecPack.SpecModels;

namespace SupportConcierge.SpecPack;

public class SpecPackLoader
{
    private readonly string _specDir;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    public SpecPackLoader(string? specDir = null)
    {
        _specDir = specDir ?? Environment.GetEnvironmentVariable("SUPPORTBOT_SPEC_DIR") ?? ".supportbot";
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public async Task<SpecPackConfig> LoadSpecPackAsync()
    {
        Console.WriteLine($"Loading SpecPack from: {_specDir}");

        var config = new SpecPackConfig();

        // Load categories
        var categoriesPath = Path.Combine(_specDir, "categories.yaml");
        if (File.Exists(categoriesPath))
        {
            var yaml = await File.ReadAllTextAsync(categoriesPath);
            var categoriesData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            if (categoriesData.ContainsKey("categories"))
            {
                var categoriesYaml = _yamlSerializer.Serialize(categoriesData["categories"]);
                config.Categories = _yamlDeserializer.Deserialize<List<Category>>(categoriesYaml);
                Console.WriteLine($"Loaded {config.Categories.Count} categories");
            }
        }

        // Load checklists
        var checklistsPath = Path.Combine(_specDir, "checklists.yaml");
        if (File.Exists(checklistsPath))
        {
            var yaml = await File.ReadAllTextAsync(checklistsPath);
            var checklistsData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yaml);
            
            if (checklistsData.ContainsKey("checklists"))
            {
                var checklistsList = checklistsData["checklists"] as List<object>;
                if (checklistsList != null)
                {
                    foreach (var item in checklistsList)
                    {
                        var checklistYaml = _yamlSerializer.Serialize(item);
                        var checklist = _yamlDeserializer.Deserialize<CategoryChecklist>(checklistYaml);
                        config.Checklists[checklist.Category] = checklist;
                    }
                    Console.WriteLine($"Loaded {config.Checklists.Count} checklists");
                }
            }
        }

        // Load validators
        var validatorsPath = Path.Combine(_specDir, "validators.yaml");
        if (File.Exists(validatorsPath))
        {
            var yaml = await File.ReadAllTextAsync(validatorsPath);
            config.Validators = _yamlDeserializer.Deserialize<ValidatorRules>(yaml);
            Console.WriteLine($"Loaded validator rules");
        }

        // Load routing
        var routingPath = Path.Combine(_specDir, "routing.yaml");
        if (File.Exists(routingPath))
        {
            var yaml = await File.ReadAllTextAsync(routingPath);
            config.Routing = _yamlDeserializer.Deserialize<RoutingRules>(yaml);
            Console.WriteLine($"Loaded {config.Routing.Routes.Count} routing rules");
        }

        // Load playbooks
        var playbooksDir = Path.Combine(_specDir, "playbooks");
        if (Directory.Exists(playbooksDir))
        {
            var playbookFiles = Directory.GetFiles(playbooksDir, "*.md");
            foreach (var file in playbookFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file);
                config.Playbooks[name] = content;
            }
            Console.WriteLine($"Loaded {config.Playbooks.Count} playbooks");
        }

        return config;
    }
}
