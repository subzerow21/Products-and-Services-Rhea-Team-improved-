using System.ComponentModel.DataAnnotations;

namespace MyAspNetApp.Data
{
    public class DbConsumer
    {
        [Key]
        public int ConsumerId { get; set; }
    }
}
