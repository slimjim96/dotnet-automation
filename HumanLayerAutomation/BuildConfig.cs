namespace HumanLayerAutomation;

/// <summary>
/// Configuration for automated build scenarios and decision strategies.
/// </summary>
public class BuildConfig
{
    /// <summary>
    /// The scenario type for this build.
    /// </summary>
    public BuildScenario Scenario { get; set; } = BuildScenario.NewRepo;

    /// <summary>
    /// The decision-making strategy for choices during the build.
    /// </summary>
    public DecisionStrategy Strategy { get; set; } = DecisionStrategy.Balanced;

    /// <summary>
    /// The scope of work to perform.
    /// </summary>
    public BuildScope Scope { get; set; } = BuildScope.FullBuild;

    /// <summary>
    /// Path to the target repository/directory.
    /// </summary>
    public required string TargetPath { get; set; }

    /// <summary>
    /// Description of what to build/update.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Specific component to focus on (when Scope is SingleComponent).
    /// </summary>
    public string? FocusComponent { get; set; }

    /// <summary>
    /// Maximum cost in USD before stopping (0 = unlimited).
    /// </summary>
    public decimal MaxCostUsd { get; set; } = 0;

    /// <summary>
    /// Whether to run completely autonomously without pauses.
    /// </summary>
    public bool FullyAutonomous { get; set; } = true;

    /// <summary>
    /// Model to use (haiku for speed/cost, sonnet for balance, opus for quality).
    /// </summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>
    /// Maximum retries per step.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Maximum build fix attempts.
    /// </summary>
    public int MaxBuildFixes { get; set; } = 3;

    /// <summary>
    /// Get a strategy-specific system prompt addition.
    /// </summary>
    public string GetStrategyPrompt() => Strategy switch
    {
        DecisionStrategy.FirstOption => @"
DECISION STRATEGY: FIRST OPTION
- When faced with multiple approaches, choose the first reasonable option
- Don't overthink or compare alternatives extensively
- Move quickly, prefer action over analysis
- If something works, use it",

        DecisionStrategy.Efficiency => @"
DECISION STRATEGY: EFFICIENCY FOCUS
- Minimize code complexity and file count
- Choose the simplest implementation that works
- Avoid over-engineering or future-proofing
- Prefer built-in language features over external dependencies
- Write concise code, avoid verbose patterns",

        DecisionStrategy.Thorough => @"
DECISION STRATEGY: THOROUGH BUILD
- Implement complete functionality with proper error handling
- Add input validation and edge case handling
- Include helpful user feedback and error messages
- Consider maintainability and code organization
- Document complex logic with comments",

        DecisionStrategy.Balanced => @"
DECISION STRATEGY: BALANCED
- Make reasonable trade-offs between speed and quality
- Implement core functionality fully
- Add basic error handling for common cases
- Keep code clean but don't over-engineer",

        _ => ""
    };

    /// <summary>
    /// Get a scope-specific instruction.
    /// </summary>
    public string GetScopePrompt() => Scope switch
    {
        BuildScope.SingleComponent => $@"
BUILD SCOPE: SINGLE COMPONENT
- Focus ONLY on: {FocusComponent ?? "the first/main component"}
- Do not implement other parts of the system
- Create stubs/interfaces for dependencies if needed
- This is an incremental build - other parts will come later",

        BuildScope.CoreOnly => @"
BUILD SCOPE: CORE FUNCTIONALITY
- Implement only the essential core features
- Skip nice-to-have features and polish
- Create a working foundation that can be extended later
- Prioritize the main user flow",

        BuildScope.FullBuild => @"
BUILD SCOPE: FULL BUILD
- Implement all specified features completely
- Include proper error handling and validation
- Add user-friendly output and help text
- Create a production-ready result",

        _ => ""
    };

    /// <summary>
    /// Get a scenario-specific instruction.
    /// </summary>
    public string GetScenarioPrompt() => Scenario switch
    {
        BuildScenario.NewRepo => @"
SCENARIO: NEW REPOSITORY
- Creating a new project from scratch
- Establish clean architecture and conventions
- Create all necessary files (project file, source files, etc.)
- No existing code to consider",

        BuildScenario.UpdateRepo => @"
SCENARIO: UPDATE EXISTING REPOSITORY
- Modifying existing code in an established project
- IMPORTANT: Match existing code style and conventions
- Preserve existing functionality unless explicitly changing it
- Make minimal changes to achieve the goal
- Don't refactor unrelated code",

        BuildScenario.AddToRepo => @"
SCENARIO: ADD TO EXISTING REPOSITORY
- Adding new features/files to an existing project
- IMPORTANT: Follow existing patterns and conventions
- Integrate with existing code structure
- Use existing utilities/helpers where appropriate
- Add new files in appropriate locations",

        _ => ""
    };
}

/// <summary>
/// The type of build scenario.
/// </summary>
public enum BuildScenario
{
    /// <summary>Create a new repository/project from scratch.</summary>
    NewRepo,

    /// <summary>Update/modify existing code in a repository.</summary>
    UpdateRepo,

    /// <summary>Add new features/components to an existing repository.</summary>
    AddToRepo
}

/// <summary>
/// Strategy for making decisions during the build process.
/// </summary>
public enum DecisionStrategy
{
    /// <summary>Choose the first reasonable option, move fast.</summary>
    FirstOption,

    /// <summary>Focus on minimal, efficient implementations.</summary>
    Efficiency,

    /// <summary>Build thoroughly with full error handling and polish.</summary>
    Thorough,

    /// <summary>Balance between speed and quality (default).</summary>
    Balanced
}

/// <summary>
/// Scope of work for the build.
/// </summary>
public enum BuildScope
{
    /// <summary>Build only a single component.</summary>
    SingleComponent,

    /// <summary>Build only core functionality.</summary>
    CoreOnly,

    /// <summary>Build everything specified.</summary>
    FullBuild
}
