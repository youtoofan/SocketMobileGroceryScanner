using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SocketMobileGroceryScanner.Models;

public class InventoryItem
{
    [Key]
    public int Id { get; set; }
    public int ProductId { get; set; }
    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
