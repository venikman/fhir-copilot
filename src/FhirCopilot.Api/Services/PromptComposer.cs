using System.Text;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public static class PromptComposer
{
    private const string FhirDataModel = """
        FHIR Data Model:
        - Group — Attribution lists. Contains member references -> Patient.
        - Patient — Demographics, address. Has generalPractitioner -> PractitionerRole, managingOrganization -> Organization.
        - Practitioner — Providers (doctors, nurses). Name, NPI, qualifications.
        - PractitionerRole — Links Practitioner -> Organization + specialty + location.
        - Organization — Clinics, hospitals, payers. Name, address, type.
        - Coverage — Insurance. payor -> Organization, beneficiary -> Patient, period, status.
        - Encounter — Visits. subject -> Patient, participant -> Practitioner, type (CPT), reasonCode (ICD-10), location, period, status.
        - Condition — Diagnoses. subject -> Patient, code (ICD-10), clinicalStatus, category.
        - Observation — Labs & vitals. subject -> Patient, code (LOINC), valueQuantity, effectiveDateTime, category.
        - MedicationRequest — Prescriptions. subject -> Patient, medicationCodeableConcept (RxNorm), status, authoredOn.
        - Procedure — Performed procedures. subject -> Patient, code (CPT), status, performedDateTime.
        - AllergyIntolerance — Allergies. patient -> Patient, code, clinicalStatus, criticality.
        """;

    public static string Compose(AgentProfile profile)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"You are the {profile.DisplayName} agent for a provider-population copilot working over FHIR R4.");
        builder.AppendLine($"Purpose: {profile.Purpose}");
        builder.AppendLine();
        builder.AppendLine(FhirDataModel);
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
        builder.AppendLine("- Lead with the direct answer.");
        builder.AppendLine("- Include resource citations when evidence is available.");
        builder.AppendLine("- Include a short reasoning summary.");

        if (profile.StructuredSections.Count > 0)
        {
            builder.AppendLine("- Use these sections when relevant:");
            foreach (var section in profile.StructuredSections)
            {
                builder.AppendLine($"  - {section}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Never dump raw JSON when a plain-English synthesis is possible.");

        return builder.ToString();
    }
}
