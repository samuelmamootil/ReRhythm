namespace ReRhythm.Core.Models;

public class LessonPlan
{
    public string UserId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public int DayNumber { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public List<Flashcard> Flashcards { get; set; } = [];
    public List<QuizQuestion> Quiz { get; set; } = [];
    public string MiniLabDescription { get; set; } = string.Empty;
    public string OfficialDocUrl { get; set; } = string.Empty;
    public bool IsCompleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Flashcard
{
    public string Front { get; set; } = string.Empty; // Question / Term
    public string Back { get; set; } = string.Empty;  // Answer / Definition
    public string SourceRef { get; set; } = string.Empty;
}

public class QuizQuestion
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = [];
    public int CorrectOptionIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
