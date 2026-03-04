using Xunit;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;
using System.Reflection;

namespace ReRhythm.Tests;

public class TextractServiceTests
{
    [Fact]
    public void ParseResumeData_ShouldExtractBasicInfo()
    {
        // Arrange
        var resumeText = @"
John Doe
john.doe@email.com | (555) 123-4567 | linkedin.com/in/johndoe
New York, NY

SUMMARY
Software engineer with 3 years experience in web development.

TECHNICAL SKILLS
Languages: Python, JavaScript, Java
Cloud: AWS, Azure
Frameworks: React, Django

EXPERIENCE
Software Engineer
Tech Company | Jan 2021 - Present
• Developed web applications using React and Python
• Improved system performance by 30%

EDUCATION
Bachelor of Science in Computer Science
University of Technology | 2017-2021 | GPA: 3.8/4.0
Relevant Coursework: Data Structures, Algorithms, Database Systems

PROJECTS
E-commerce Platform | React, Node.js, MongoDB
• Built full-stack web application
• Implemented user authentication and payment processing

CERTIFICATIONS
• AWS Certified Developer Associate
• Google Cloud Professional Developer
";

        // Act
        var service = new TextractService(null, null, null, null);
        var parseMethod = typeof(TextractService).GetMethod("ParseResumeData", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ResumeData)parseMethod.Invoke(service, new object[] { resumeText });

        // Assert
        Assert.Equal("John Doe", result.Name);
        Assert.Equal("john.doe@email.com", result.Email);
        Assert.Equal("(555) 123-4567", result.Phone);
        Assert.Contains("linkedin.com/in/johndoe", result.LinkedIn);
        Assert.Contains("New York, NY", result.Location);
        Assert.Contains("Python", result.TechnicalSkills);
        Assert.Contains("JavaScript", result.TechnicalSkills);
        Assert.Single(result.WorkHistory);
        Assert.Equal("Software Engineer", result.WorkHistory[0].Role);
        Assert.Equal("Tech Company", result.WorkHistory[0].Company);
        Assert.Single(result.Education);
        Assert.Equal("University of Technology", result.Education[0].Institution);
        Assert.Equal("3.8/4.0", result.Education[0].GPA);
        Assert.Single(result.PersonalProjects);
        Assert.Equal("E-commerce Platform", result.PersonalProjects[0].Name);
        Assert.Contains("AWS Certified Developer Associate", result.Certifications);
    }
}