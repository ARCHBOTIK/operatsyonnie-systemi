namespace SecurePassword;

public class LoginInfo : IHasID
{
    public int Id { get; set; }
    public string Password { get; set; }
    public string Username { get; set; }
}