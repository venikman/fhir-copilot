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

    private const string CodeSystems = """
        Code Systems (use system|code format in searches):
        - ICD-10-CM (diagnoses): E11.* (Type 2 diabetes), I10 (Hypertension), J06.9 (URI), M54.5 (Low back pain)
        - CPT (procedures/encounters): 99213 (Office visit), 99385 (Preventive visit)
        - LOINC (observations): 4548-4 (HbA1c), 2339-0 (Glucose), 8480-6 (Systolic BP), 8462-4 (Diastolic BP)
        - RxNorm (medications): 860975 (Metformin), 310798 (Lisinopril), 197361 (Amlodipine)
        """;

    public static string Compose(AgentProfile profile)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"You are the {profile.DisplayName} agent for a provider-population copilot working over FHIR R4.");
        builder.AppendLine($"Purpose: {profile.Purpose}");
        builder.AppendLine();
        builder.AppendLine(FhirDataModel);
        builder.AppendLine(CodeSystems);
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
        builder.AppendLine("- Cite resource IDs (e.g. Patient/patient-0001) for traceability.");
        builder.AppendLine("- Show reference chains when resolving (e.g. Patient -> Practitioner -> Organization).");
        builder.AppendLine("- Format tables for directory/comparison queries.");
        builder.AppendLine("- Direct answer first for yes/no questions, then evidence.");
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
