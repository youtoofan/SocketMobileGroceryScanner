using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocketMobileGroceryScanner.Models;

public class ShoppingListItem
{
    [Key]
    public int Id { get; set; }
    public int ProductId { get; set; }
    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
    public int NeededQty { get; set; } = 1;
    public bool IsFound { get; set; } = false;
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Id of the linked Microsoft To Do task, if this item has been synced.
    /// Used to reliably detect additions/removals made directly in Microsoft To Do.
    /// </summary>
    public string? ToDoTaskId { get; set; }
}
