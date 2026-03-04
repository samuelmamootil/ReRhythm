using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text;
using System.Text.Json;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class ResumeBuilderService
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly string _modelId;

    public ResumeBuilderService(IAmazonBedrockRuntime bedrockClient, string modelId)
    {
        _bedrockClient = bedrockClient;
        _modelId = modelId;
    }

    public async Task<string> GenerateEnhancedResumeAsync(
        ResumeData originalResume,
        List<string> skillsLearned,
        string targetRole,
        string industry,
        List<string> completedProjects)
    {
        try
        {
            var prompt = BuildResumePrompt(originalResume, skillsLearned, targetRole, industry, completedProjects);

            var request = new InvokeModelRequest
            {
                ModelId = _modelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 4096,
                    temperature = 0.3,
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = prompt
                        }
                    }
                })))
            };

            var response = await _bedrockClient.InvokeModelAsync(request);
            using var reader = new StreamReader(response.Body);
            var responseBody = await reader.ReadToEndAsync();
            var jsonResponse = JsonDocument.Parse(responseBody);
            
            return jsonResponse.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to generate enhanced resume: {ex.Message}", ex);
        }
    }

    private string BuildResumePrompt(
        ResumeData originalResume,
        List<string> skillsLearned,
        string targetRole,
        string industry,
        List<string> completedProjects)
    {
        var persona = DeterminePersona(originalResume.YearsExperience, originalResume.ParsedText);
        var pageLimit = originalResume.YearsExperience <= 5 ? "1 page" : "2 pages max";
        
        var originalInfo = $@"
**ORIGINAL RESUME DATA:**
Name: {originalResume.Name}
Email: {originalResume.Email}
Phone: {originalResume.Phone}
LinkedIn: {originalResume.LinkedIn}
GitHub: {originalResume.GitHub}
Website: {originalResume.Website}
Location: {originalResume.Location}

Professional Summary:
{originalResume.ProfessionalSummary}

Technical Skills:
{string.Join(", ", originalResume.TechnicalSkills)}

Work Experience:
{string.Join("\n", originalResume.WorkHistory.Select(w => $"{w.Role} at {w.Company} ({w.Duration})\n{string.Join("\n", w.Responsibilities.Select(r => $"  • {r}"))}"))}

Education:
{string.Join("\n", originalResume.Education.Select(e => $"{e.Degree} - {e.Institution} ({e.Duration}) {(string.IsNullOrEmpty(e.GPA) ? "" : $"GPA: {e.GPA}")}\n{(string.IsNullOrEmpty(e.Coursework) ? "" : $"Coursework: {e.Coursework}")}"))}

Personal Projects:
{string.Join("\n", originalResume.PersonalProjects.Select(p => $"{p.Name} | {p.TechStack}\n{string.Join("\n", p.Description.Select(d => $"  • {d}"))}"))}

Certifications:
{string.Join("\n", originalResume.Certifications.Select(c => $"• {c}"))}

Extracurricular Activities:
{string.Join("\n", originalResume.ExtracurricularActivities.Select(a => $"• {a}"))}

Student Organizations:
{string.Join("\n", originalResume.StudentOrganizations.Select(o => $"• {o}"))}
";
        
        return $@"You are an expert ATS resume writer. Generate an enhanced, ATS-optimized resume based on the following:

{originalInfo}

**NEW SKILLS LEARNED (from 28-day ReRhythm program):**
{string.Join(", ", skillsLearned)}

**TARGET ROLE:** {targetRole}
**INDUSTRY:** {industry}
**YEARS OF EXPERIENCE:** {originalResume.YearsExperience}
**PERSONA:** {persona}

**COMPLETED PROJECTS (from ReRhythm roadmap):**
{string.Join("\n", completedProjects.Select((p, i) => $"{i + 1}. {p}"))}

**CRITICAL REQUIREMENTS:**

1. **Universal ATS Structure (MUST follow this EXACT order):**
   1. Contact Information (Name, Phone, Email, LinkedIn, GitHub, Website if available, City/State)
   2. Professional Summary (3-4 lines ONLY - update with new skills and target role)
   3. Technical Skills / Core Competencies (grouped by category - merge original + new skills)
   4. Work Experience (reverse chronological, CAR format - reframe with new skills)
   5. Projects (ReRhythm projects + existing projects with tech stack)
   6. Education (degree, GPA if 3.5+, relevant coursework)
   7. Certifications (with dates - include original + ReRhythm completion)
   8. Optional: Extracurricular Activities, Student Organizations, Leadership

2. **ATS Rules (NON-NEGOTIABLE):**
   - Single-column layout ONLY
   - NO tables, graphics, text boxes, or special characters
   - NO headers/footers with contact info
   - Standard section names (SUMMARY, SKILLS, EXPERIENCE, PROJECTS, EDUCATION, CERTIFICATIONS)
   - Plain text format with bullet points
   - Use standard fonts: Arial, Calibri, or Times New Roman

3. **Persona-Specific Guidelines for {persona}:**

{GetPersonaGuidelines(persona)}

4. **Skills Integration Strategy:**
   - **Technical Skills Section:** Merge original skills with new skills, group by category (Cloud, Languages, DevOps, Data, etc.)
   - **Work Experience:** Reframe existing bullets to include new skills where relevant
   - **Projects Section:** Add all {completedProjects.Count} ReRhythm projects PLUS keep original projects
   - **Summary:** Update to reflect new capabilities and target role
   - **Certifications:** Include original certifications + ReRhythm program completion

5. **Bullet Point Format (CAR - Context to Action to Result):**
   GOOD: Automated infrastructure provisioning using Terraform and AWS, reducing deployment time by 70 percent across 3 environments
   BAD: Responsible for managing AWS infrastructure
   
   - Start with strong action verbs (Engineered, Architected, Automated, Built, Developed, Led)
   - Include specific technologies used
   - Quantify results (percentages, time saved, cost reduction, scale)

6. **Content Enhancement Rules:**
   - Integrate new skills naturally into existing experience
   - Add ReRhythm projects with: Project Name | Tech Stack as headline
   - Use industry keywords from target role throughout
   - Keep resume to {pageLimit}
   - Preserve ALL original contact information (email, phone, LinkedIn, GitHub, website)
   - Preserve ALL education details (institution, degree, GPA, coursework)
   - Preserve ALL certifications and add new ones
   - Include extracurricular activities and student organizations if space permits

**OUTPUT FORMAT:**
Return ONLY the complete resume text in plain text format.
Use this exact structure:

[NAME]
[Phone] | [Email] | [LinkedIn] | [GitHub] | [Website] | [City, State]

SUMMARY
[3-4 lines integrating original experience + new skills + target role]

TECHNICAL SKILLS
[Grouped by category - merge original + new]

WORK EXPERIENCE
[Company], [Title]
[Dates]
[CAR bullet with new skills integrated]
[CAR bullet]

PROJECTS
[ReRhythm Project Name] | [Tech Stack]
[CAR bullet]

[Original Project Name] | [Tech Stack]
[CAR bullet]

EDUCATION
[Degree] [Major]
[University] | [Dates] | [GPA if 3.5+]
[Relevant Coursework if available]

CERTIFICATIONS
[Original Cert Name] - [Date]
[ReRhythm Program Completion] - [Current Date]

EXTRACURRICULAR ACTIVITIES (if space permits)
[Activity]

STUDENT ORGANIZATIONS (if space permits)
[Organization]

Begin the resume now:";
    }

    private string DeterminePersona(int yearsOfExperience, string originalResume)
    {
        var resumeLower = originalResume.ToLower();
        
        if (yearsOfExperience <= 2 || 
            resumeLower.Contains("student") || 
            resumeLower.Contains("graduate") ||
            resumeLower.Contains("master of") ||
            resumeLower.Contains("bachelor of"))
        {
            return "Graduate Student (First Role)";
        }
        
        if (yearsOfExperience >= 3 && yearsOfExperience <= 5)
        {
            return "Career Switcher Entering Tech";
        }
        
        return "Working Professional Upskilling";
    }

    private string GetPersonaGuidelines(string persona)
    {
        return persona switch
        {
            "Graduate Student (First Role)" => 
@"- Summary: 2-3 lines max. Format: [Degree] candidate with hands-on [Skills] experience seeking [Target Role]
- Technical Skills: MOST IMPORTANT SECTION. Group by: Cloud, Languages, DevOps, Data
- Projects: Treat like work experience with 3-4 CAR bullets each
- Education: Include GPA if 3.5+, relevant coursework, honors
- Length: 1 page ONLY",

            "Career Switcher Entering Tech" => 
@"- Summary: Lead with transferable value. Format: [Previous Role] with [X] years experience transitioning to [Tech Role]; proficient in [New Skills]
- Technical Skills: Explicitly list newly acquired skills grouped by category
- Projects: MOST CRITICAL SECTION. Must prove career change
- Work Experience: REFRAME old bullets using tech language
- Length: 1-2 pages",

            "Working Professional Upskilling" => 
@"- Summary: Position toward NEXT role. Format: [Current Title] with [X]+ years; expanding into [New Area] with hands-on [New Skills] experience
- Core Competencies: Two-column list of 12-16 keywords
- Work Experience: CAR format for EVERY bullet with measurable outcomes
- Certifications: Prominently placed with year obtained
- Length: 2 pages max",

            _ => ""
        };
    }

    private string GetExampleStructure(string persona)
    {
        return persona switch
        {
            "Graduate Student (First Role)" => 
@"MIKHAEL UZAGARE
+1-682-336-3821 mxu3528@mavs.uta.edu LinkedIn: Mikhael Uzagare Arlington, Texas

SUMMARY
High-achieving MSCS candidate specializing in AI, Machine Learning, and Cloud Infrastructure.

TECHNICAL SKILLS
AI/ML: Large Language Models, NLP, Scikit-Learn
Languages: Python, Java, SQL, JavaScript
Cloud: AWS (EC2, Lambda, S3), Azure, Docker",

            "Career Switcher Entering Tech" => 
@"KETKI GHADGE
+1 (817) 822-8516 ketkighadge03@gmail.com linkedin.com/in/ketkighadge Arlington, TX

SUMMARY
Information Systems Graduate with hands-on Python, SQL, and Salesforce development experience.

TECHNICAL SKILLS
Languages: Python, R, SQL, Apex Programming
Cloud: AWS (EC2, S3, Lambda), Terraform, Docker",

            "Working Professional Upskilling" => 
@"SAMUEL MAMOOTIL
samuelrajumamootil@gmail.com +1 512-563-0303 linkedin.com/in/samuelmamootil Austin, TX

SUMMARY
Senior DevOps Engineer with 8+ years; expanding into MLOps and AI/ML infrastructure.

SKILLS
AWS Architecture, CI/CD Pipelines, Terraform, Kubernetes, MLOps",

            _ => ""
        };
    }
}
