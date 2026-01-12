namespace SupportConcierge.SpecPack;

public class SpecModels
{
    public class Category
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
    }

    public class CategoryChecklist
    {
        public string Category { get; set; } = "";
        public int CompletenessThreshold { get; set; } = 70;
        public List<RequiredField> RequiredFields { get; set; } = new();
    }

    public class RequiredField
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Weight { get; set; } = 10;
        public bool Optional { get; set; } = false;
        public List<string> Aliases { get; set; } = new();
    }

    public class ValidatorRules
    {
        public List<string> JunkPatterns { get; set; } = new();
        public Dictionary<string, string> FormatValidators { get; set; } = new();
        public List<string> SecretPatterns { get; set; } = new();
        public List<ContradictionRule> ContradictionRules { get; set; } = new();
    }

    public class ContradictionRule
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Field1 { get; set; } = "";
        public string Field2 { get; set; } = "";
        public string Condition { get; set; } = "";
    }

    public class RoutingRules
    {
        public List<CategoryRoute> Routes { get; set; } = new();
        public List<string> EscalationMentions { get; set; } = new();
    }

    public class CategoryRoute
    {
        public string Category { get; set; } = "";
        public List<string> Labels { get; set; } = new();
        public List<string> Assignees { get; set; } = new();
    }

    public class SpecPackConfig
    {
        public List<Category> Categories { get; set; } = new();
        public Dictionary<string, CategoryChecklist> Checklists { get; set; } = new();
        public ValidatorRules Validators { get; set; } = new();
        public RoutingRules Routing { get; set; } = new();
        public Dictionary<string, string> Playbooks { get; set; } = new();
    }
}
