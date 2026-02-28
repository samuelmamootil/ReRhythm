using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text;
using System.Text.Json;

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
        string originalResumeText,
        List<string> skillsLearned,
        string targetRole,
        string industry,
        int yearsOfExperience,
        List<string> completedProjects)
    {
        try
        {
            var prompt = BuildResumePrompt(originalResumeText, skillsLearned, targetRole, industry, yearsOfExperience, completedProjects);

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
        string originalResume,
        List<string> skillsLearned,
        string targetRole,
        string industry,
        int yearsOfExperience,
        List<string> completedProjects)
    {
        var persona = DeterminePersona(yearsOfExperience, originalResume);
        var pageLimit = yearsOfExperience <= 5 ? "1 page" : "2 pages max";
        
        return $@"You are an expert ATS resume writer. Generate an enhanced, ATS-optimized resume based on the following:

**ORIGINAL RESUME:**
{originalResume}

**NEW SKILLS LEARNED (from 28-day ReRhythm program):**
{string.Join(", ", skillsLearned)}

**TARGET ROLE:** {targetRole}
**INDUSTRY:** {industry}
**YEARS OF EXPERIENCE:** {yearsOfExperience}
**PERSONA:** {persona}

**COMPLETED PROJECTS (from ReRhythm roadmap):**
{string.Join("\n", completedProjects.Select((p, i) => $"{i + 1}. {p}"))}

**CRITICAL REQUIREMENTS:**

1. **Universal ATS Structure (MUST follow this EXACT order):**
   1. Contact Information (Name, Phone, Email, LinkedIn, City/State)
   2. Professional Summary (3-4 lines ONLY)
   3. Technical Skills / Core Competencies (grouped by category)
   4. Work Experience (reverse chronological, CAR format)
   5. Projects (ReRhythm projects + existing projects)
   6. Education (degree, GPA if 3.5+, relevant coursework)
   7. Certifications (with dates)
   8. Optional: Honors & Awards

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
   - **Technical Skills Section:** Group skills by category (Cloud, Languages, DevOps, Data, etc.)
   - **Work Experience:** Reframe existing bullets to include new skills where relevant
   - **Projects Section:** Add all {completedProjects.Count} ReRhythm projects with tech stack and measurable results
   - **Summary:** Update to reflect new capabilities and target role

5. **Bullet Point Format (CAR - Context to Action to Result):**
   GOOD: Automated infrastructure provisioning using Terraform and AWS, reducing deployment time by 70 percent across 3 environments
   BAD: Responsible for managing AWS infrastructure
   
   - Start with strong action verbs (Engineered, Architected, Automated, Built, Developed, Led)
   - Include specific technologies used
   - Quantify results (percentages, time saved, cost reduction, scale)

6. **Example Resume Structures to Follow:**

{GetExampleStructure(persona)}

7. **Content Enhancement Rules:**
   - Integrate new skills naturally into existing experience
   - Add ReRhythm projects with: Project Name | Tech Stack as headline
   - Use industry keywords from target role throughout
   - Keep resume to {pageLimit}
   - Remove anything older than 10-12 years unless highly relevant

**OUTPUT FORMAT:**
Return ONLY the complete resume text in plain text format.
Use this exact structure:

[NAME]
[Phone] [Email] [LinkedIn] [City, State]

SUMMARY
[3-4 lines]

TECHNICAL SKILLS
[Grouped by category]

WORK EXPERIENCE
[Company], [Title]
[Dates]
[CAR bullet]
[CAR bullet]

PROJECTS
[Project Name] | [Tech Stack]
[CAR bullet]

EDUCATION
[Degree] [Major]
[University]
[GPA if 3.5+]

CERTIFICATIONS
[Cert Name] [Date]

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
