namespace SecurePassword;

public class Note : IHasID
{
    public int Id { get; set; }
    public string NoteText { get; set; }
}