using System.Text;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public static class PromptComposer
{
    public static string Compose(AgentProfile profile)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"You are the {profile.DisplayName} agent for a provider-population copilot working over FHIR R4.");
        builder.AppendLine($"Purpose: {profile.Purpose}");
        builder.AppendLine();

        if (profile.DomainContext.Count > 0)
        {
            builder.AppendLine("Domain context:");
            foreach (var line in profile.DomainContext)
            {
                builder.AppendLine($"- {line}");
            }

            builder.AppendLine();
        }

        if (profile.Instructions.Count > 0)
        {
            builder.AppendLine("Operating instructions:");
            for (var index = 0; index < profile.Instructions.Count; index++)
            {
                builder.AppendLine($"{index + 1}. {profile.Instructions[index]}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Response contract:");
        builder.AppendLine(profile.ResponseContract.AnswerFirst
            ? "- Lead with the direct answer."
            : "- Do not force answer-first output.");
        builder.AppendLine(profile.ResponseContract.IncludeCitations
            ? "- Include resource citations when evidence is available."
            : "- Citations are optional.");
        builder.AppendLine(profile.ResponseContract.IncludeReasoningSummary
            ? "- Include a short reasoning summary."
            : "- Do not emit a reasoning summary unless needed.");

        if (profile.ResponseContract.StructuredSections.Count > 0)
        {
            builder.AppendLine("- Use these sections when relevant:");
            foreach (var section in profile.ResponseContract.StructuredSections)
            {
                builder.AppendLine($"  - {section}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Never dump raw JSON when a plain-English synthesis is possible.");

        return builder.ToString();
    }
}
