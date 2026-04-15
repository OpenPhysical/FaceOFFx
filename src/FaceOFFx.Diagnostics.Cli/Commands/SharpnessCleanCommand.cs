using System.ComponentModel;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

[Description("Clean generated sharpness artifacts")]
internal sealed class SharpnessCleanCommand : Command<SharpnessCleanCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--all")]
        [Description("Remove all generated sharpness artifacts")]
        public bool All { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var target = Path.Combine("artifacts", "diagnostics", "sharpness");
        if (settings.All && Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        return 0;
    }
}
