using Microsoft.EntityFrameworkCore;

namespace BuzzKeepr.Infrastructure.Persistence;

public sealed class BuzzKeeprDbContext(DbContextOptions<BuzzKeeprDbContext> options) : DbContext(options)
{
}
