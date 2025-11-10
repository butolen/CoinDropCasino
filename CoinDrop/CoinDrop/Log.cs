namespace CoinDrop;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public enum LogActionType
{
    Info,
    Warning,
    Error,
    AdminAction,
    UserAction
}

public enum LogUserType
{
    User,
    Admin,
    System
}

[Table("log")]
public class Log
{
    [Key]
    [Column("log_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int LogId { get; set; }

    [Column("action_type", TypeName = "varchar(30)")]
    public LogActionType ActionType { get; set; }

    [Column("user_type", TypeName = "varchar(20)")]
    public LogUserType UserType { get; set; }

    [Column("user_id")]
    public int? UserId { get; set; }

    [Column("date", TypeName = "datetime")]
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Column("description", TypeName = "text")]
    public string Description { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }
}