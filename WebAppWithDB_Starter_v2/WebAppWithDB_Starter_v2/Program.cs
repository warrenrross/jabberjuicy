using Microsoft.Data.SqlClient;
using System.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebAppWithDB_Starter_v2
{
    public class Program
    {
        private static IConfiguration _config = null!;

        // ─── Cart item: stored in session as JSON ─────────────────────────────
        private record CartItem(int ItemID, string Name, decimal UnitPrice, int Quantity);

        // ─── JabberWonk Points constants ──────────────────────────────────────
        private const int JWP_PointsPerVisit   = 50;   // flat bonus per qualifying order
        private const int JWP_PointsPerDollar  = 10;   // pts per whole dollar spent
        private const int JWP_PointsPerRedempt = 100;  // pts required to redeem $1.00
        private const int JWP_MaxEarnPerVisit  = 2500; // per-order earn cap

        // ─── Order expiry ──────────────────────────────────────────────────────
        private const int ORDER_EXPIRY_MINUTES = 60;                              // cancel Pending orders older than 1 hour
        private static readonly TimeSpan ORDER_CLEANUP_INTERVAL = TimeSpan.FromHours(2); // poll 12x per day

        // ─────────────────────────────────────────────────────────────────────
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddLogging(logging => logging.AddConsole());
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(o =>
            {
                o.IdleTimeout = TimeSpan.FromMinutes(30);
                o.Cookie.HttpOnly = true;
                o.Cookie.IsEssential = true;
            });
            builder.Services.AddHostedService<ExpiredOrderCleanupService>();

            var app = builder.Build();
            _config = app.Configuration;

            if (!app.Environment.IsDevelopment())
                app.UseExceptionHandler("/error");

            app.Use(async (context, next) =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
                context.Response.Headers.XFrameOptions = "DENY";
                context.Response.Headers.ContentSecurityPolicy =
                    "default-src 'self';" +
                    "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://ajax.googleapis.com;" +
                    "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com;" +
                    "img-src 'self' data:;" +
                    "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com data:;" +
                    "connect-src 'self';" +
                    "frame-ancestors 'none';" +
                    "base-uri 'self';" +
                    "form-action 'self';";
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                await next(context);
            });

            app.UseStaticFiles();
            app.UseSession();
            MapRoutes(app);
            app.Run();
        }

        // ─── ROUTES ──────────────────────────────────────────────────────────

        private static void MapRoutes(WebApplication app)
        {
            app.MapGet("/",             HandleLanding);
            app.MapGet("/admin",        HandleAdminDashboard);
            app.MapGet("/admin/login",  HandleAdminLoginGet);
            app.MapPost("/admin/login",         HandleAdminLoginPost);
            app.MapPost("/admin/points-adjust", HandleAdminPointsAdjust);
            app.MapGet("/admin/logout",         HandleAdminLogout);
            app.MapGet("/login",        HandleLoginGet);
            app.MapPost("/login",       HandleLoginPost);
            app.MapGet("/register",     HandleRegisterGet);
            app.MapPost("/register",    HandleRegisterPost);
            app.MapGet("/logout",       HandleLogout);
            app.MapGet("/home",         HandleHome);
            app.MapGet("/menu",         HandleMenu);
            app.MapGet("/order",        HandleOrderGet);
            app.MapPost("/order/add",   HandleOrderAdd);
            app.MapPost("/order/remove",(Delegate)HandleOrderRemove);
            app.MapPost("/order/clear", (Delegate)HandleOrderClear);
            app.MapGet("/checkout",     HandleCheckoutGet);
            app.MapPost("/checkout",    HandleCheckoutPost);
            app.MapGet("/receipt",      HandleReceipt);
            app.MapGet("/history",      HandleHistory);
            app.MapGet("/error",        HandleError);
            app.MapGet("/account",          HandleAccountGet);
            app.MapPost("/account",         HandleAccountPost);
            app.MapGet("/account/success",  HandleAccountSuccess);
            app.MapGet("/pickup",                        HandlePickupIndex);
            app.MapGet("/pickup/{orderId:int}",          HandlePickupOrder);
            app.MapPost("/pickup/{orderId:int}/confirm", HandlePickupConfirm);
            app.MapPost("/pickup/{orderId:int}/cancel",  HandlePickupCancel);
            app.MapGet("/pickup/{orderId:int}/success",  HandlePickupSuccess);
        }

        // ─── LAYOUT HELPERS ──────────────────────────────────────────────────

        private static string PageHead(string title) => $@"<!doctype html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>{H(title)} | JabberJuicy</title>
  <meta name='robots' content='noindex, nofollow'>
  <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css'
        rel='stylesheet'
        integrity='sha384-sRIl4kxILFvY47J16cr9ZwB07vP4J8+LH7qKQnuqkuIAvNWLzeN8tE5YBujZqJLB'
        crossorigin='anonymous'>
  <link rel='icon' href='/brand/favicons/favicon.ico' sizes='any'>
  <link rel='icon' type='image/png' sizes='16x16' href='/brand/favicons/favicon-16x16.png'>
  <link rel='icon' type='image/png' sizes='32x32' href='/brand/favicons/favicon-32x32.png'>
  <link rel='apple-touch-icon' href='/brand/favicons/apple-touch-icon.png'>
  <link rel='manifest' href='/brand/tokens/site.webmanifest'>
  <style>
    .jj-brand {{ font-family: Georgia, 'Times New Roman', serif; letter-spacing: 1px; }}
    .jj-hero  {{ background: linear-gradient(135deg, #f97316 0%, #ea580c 55%, #16a34a 100%); }}
    .jj-nav   {{ background-color: #ea580c !important; }}
    body {{ background-color: #f8f9fa; }}
    @import url('https://fonts.googleapis.com/css2?family=Cinzel+Decorative:wght@700&family=Lato:wght@400;500&display=swap');
    :root {{
      --jj-forest: #2e5a3d;
      --jj-plum: #6e2c91;
      --jj-citrus: #f28c1b;
      --jj-cream: #f4ead4;
      --jj-berry: #d94e8f;
      --jj-teal: #09a7a7;
      --jj-copper: #8b5036;
      --jj-charcoal: #1e1e1e;
      --jj-font-display: 'Cinzel Decorative', Georgia, serif;
      --jj-font-body: 'Lato', 'Helvetica Neue', Arial, sans-serif;
      --jj-radius-sm: 12px;
      --jj-radius-md: 20px;
      --jj-radius-lg: 28px;
      --jj-shadow-soft: 0 10px 30px rgba(30, 30, 30, 0.12);
    }}
    .jj-display {{ font-family: var(--jj-font-display); }}
    .jj-button-primary {{ background: var(--jj-plum); color: #fff; border-radius: var(--jj-radius-sm); }}
    .jj-button-secondary {{ background: #fff; color: var(--jj-copper); border: 1px solid rgba(139, 80, 54, 0.45); border-radius: var(--jj-radius-sm); }}
    .jj-button-tertiary {{ background: var(--jj-forest); color: #fff; border-radius: var(--jj-radius-sm); }}
  </style>
</head>
<body>";

        private static string PageFoot() => @"
  <script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/js/bootstrap.bundle.min.js'
          integrity='sha384-FKyoEForCGlyvwx9Hj09JcYn3nv7wiPVlz7YYwJrWVcXK/BmnVDxM+D2scQbITxI'
          crossorigin='anonymous'></script>
</body>
</html>";

        private static string NavBar(string? username, int? pointsBalance = null, int pendingCount = 0)
        {
            string demoAdminPill =
                "<li class='nav-item'><a class='nav-link px-2' href='/admin/login'>" +
                "<span class='badge rounded-pill px-3 py-2 fw-semibold border border-danger bg-white' " +
                "style='color:#b91c1c;'>Demo Admin</span></a></li>";
            string pointsBadge = (username != null && pointsBalance.HasValue)
                ? $"<li class='nav-item'><span class='navbar-text ms-1'>" +
                  $"<span class='badge rounded-pill' style='background:#fbbf24;color:#1a1a1a;'>" +
                  $"&#11088; {pointsBalance.Value:N0} pts</span></span></li>"
                : "";
            string pickupBadge = (username != null && pendingCount > 0)
                ? "<li class='nav-item'><a class='nav-link px-2' href='/pickup'>" +
                  "<span class='badge bg-success px-3 py-2'>" +
                  (pendingCount == 1 ? "&#x1F7E2; Pickup Ready" : $"&#x1F7E2; {pendingCount} Pickups Ready") +
                  "</span></a></li>"
                : "";
            string authLinks = username != null
                ? $@"<li class='nav-item'><a class='nav-link text-white' href='/menu'>Menu</a></li>
                     <li class='nav-item'><a class='nav-link text-white' href='/order'>New Order</a></li>
                     {pickupBadge}
                     <li class='nav-item dropdown'>
                       <a class='nav-link dropdown-toggle fw-semibold' style='color:#fde68a;'
                          href='#' role='button' data-bs-toggle='dropdown' aria-expanded='false'>
                         Hi, {H(username)}!
                       </a>
                       <ul class='dropdown-menu dropdown-menu-end'>
                         <li><a class='dropdown-item' href='/account'>Change Username</a></li>
                         <li><a class='dropdown-item' href='/history'>View Orders</a></li>
                       </ul>
                     </li>
                     {pointsBadge}
                     <li class='nav-item'><a class='nav-link text-white' href='/logout'>Logout</a></li>
                     {demoAdminPill}"
                : @"<li class='nav-item'><a class='nav-link text-white' href='/menu'>Menu</a></li>
                    <li class='nav-item'><a class='nav-link text-white' href='/login'>Login</a></li>
                    <li class='nav-item'><a class='nav-link text-white' href='/register'>Register</a></li>
                    " + demoAdminPill;

            return $@"
<nav class='navbar navbar-expand-lg jj-nav shadow-sm'>
  <div class='container'>
    <a class='navbar-brand d-flex align-items-center gap-2' href='/'><img src='/brand/icons/icon-round-color.png' height='40' style='width:auto;' alt='JabberJuicy'><span class='text-white fw-bold fs-4 jj-brand'>JabberJuicy</span></a>
    <button class='navbar-toggler border-light' type='button'
            data-bs-toggle='collapse' data-bs-target='#navMain'
            aria-controls='navMain' aria-expanded='false' aria-label='Toggle navigation'>
      <span class='navbar-toggler-icon'></span>
    </button>
    <div class='collapse navbar-collapse' id='navMain'>
      <ul class='navbar-nav ms-auto mb-2 mb-lg-0'>
        {authLinks}
      </ul>
    </div>
  </div>
</nav>";
        }

        private static string Layout(string title, string? username, string body, int? points = null, int pendingCount = 0)
            => PageHead(title) + NavBar(username, points, pendingCount) + body + PageFoot();

        // HTML-encode helper (shorthand)
        private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

        // ─── AUTH HELPERS ─────────────────────────────────────────────────────

        private static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
            byte[] combined = new byte[48];
            Array.Copy(salt, 0, combined, 0, 16);
            Array.Copy(hash, 0, combined, 16, 32);
            return Convert.ToBase64String(combined);
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            try
            {
                byte[] combined = Convert.FromBase64String(storedHash);
                if (combined.Length != 48) return false;
                byte[] salt = new byte[16];
                Array.Copy(combined, 0, salt, 0, 16);
                byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
                for (int i = 0; i < 32; i++)
                    if (combined[i + 16] != hash[i]) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool IsAuthenticated(HttpContext ctx) =>
            ctx.Session.GetString("uid") != null;

        private static int GetCurrentUserId(HttpContext ctx) =>
            int.TryParse(ctx.Session.GetString("uid"), out int id) ? id : 0;

        private static string? GetCurrentUsername(HttpContext ctx) =>
            ctx.Session.GetString("uname");

        private static bool IsAdminAuthenticated(HttpContext ctx) =>
            ctx.Session.GetString("admin_uid") != null;

        private static int GetCurrentAdminId(HttpContext ctx) =>
            int.TryParse(ctx.Session.GetString("admin_uid"), out int id) ? id : 0;

        private static string? GetCurrentAdminUsername(HttpContext ctx) =>
            ctx.Session.GetString("admin_uname");

        private static string? GetCurrentAdminDisplayName(HttpContext ctx) =>
            ctx.Session.GetString("admin_display");

        private static string? GetCurrentAdminRole(HttpContext ctx) =>
            ctx.Session.GetString("admin_role");

        private static bool GetCurrentAdminIsGlobalAccess(HttpContext ctx) =>
            ctx.Session.GetString("admin_global") == "1";

        private static void SignInAdminSession(HttpContext ctx, DataRow row)
        {
            ctx.Session.SetString("admin_uid", row["AdminUserID"].ToString()!);
            ctx.Session.SetString("admin_uname", row["ADM_Username"].ToString()!);
            ctx.Session.SetString("admin_display", row["ADM_DisplayName"].ToString()!);
            ctx.Session.SetString("admin_role", row["ADM_Role"].ToString()!);
            ctx.Session.SetString("admin_global",
                Convert.ToBoolean(row["ADM_IsGlobalAccess"]) ? "1" : "0");
        }

        private static void ClearAdminSession(HttpContext ctx)
        {
            ctx.Session.Remove("admin_uid");
            ctx.Session.Remove("admin_uname");
            ctx.Session.Remove("admin_display");
            ctx.Session.Remove("admin_role");
            ctx.Session.Remove("admin_global");
        }

        private static int? ParseNullableInt(string? value) =>
            int.TryParse(value, out int id) ? id : null;

        private static string BuildAdminUrl(string grain, int? locationId, string? q = null,
            int? customerId = null, int? orderId = null, string? metricRange = null)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(grain))
                parts.Add($"grain={Uri.EscapeDataString(grain)}");
            if (!string.IsNullOrWhiteSpace(metricRange))
                parts.Add($"metricRange={Uri.EscapeDataString(metricRange)}");
            if (locationId.HasValue)
                parts.Add($"locationId={locationId.Value}");
            if (!string.IsNullOrWhiteSpace(q))
                parts.Add($"q={Uri.EscapeDataString(q)}");
            if (customerId.HasValue)
                parts.Add($"customerId={customerId.Value}");
            if (orderId.HasValue)
                parts.Add($"orderId={orderId.Value}");

            return parts.Count == 0 ? "/admin" : "/admin?" + string.Join("&", parts);
        }

        private static async Task<bool> UsernameExistsAsync(string username, ILogger? logger = null)
        {
            var cmd = new SqlCommand("SELECT COUNT(1) FROM Customer WHERE CUS_Username = @u");
            cmd.Parameters.AddWithValue("@u", username);
            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            return dt != null && Convert.ToInt32(dt.Rows[0][0]) > 0;
        }

        private static async Task<int> GetPointsBalanceAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand("SELECT CUS_PointsBalance FROM Customer WHERE CustomerID = @id");
            cmd.Parameters.AddWithValue("@id", customerId);
            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0) return 0;
            return dt.Rows[0]["CUS_PointsBalance"] == DBNull.Value ? 0
                : Convert.ToInt32(dt.Rows[0]["CUS_PointsBalance"]);
        }

        private static async Task<int> GetJabberWonkPaymentTypeIdAsync(ILogger? logger = null)
        {
            var cmd = new SqlCommand(
                "SELECT PaymentTypeID FROM PaymentType WHERE PAY_TypeName = 'JabberWonk Points'");
            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0) return -1;
            return Convert.ToInt32(dt.Rows[0]["PaymentTypeID"]);
        }

        private static async Task<int> GetPendingOrderCountAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM [Order] WHERE CustomerID = @id AND ORD_Status = 'Pending'");
            cmd.Parameters.AddWithValue("@id", customerId);
            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0) return 0;
            return dt.Rows[0][0] == DBNull.Value ? 0 : Convert.ToInt32(dt.Rows[0][0]);
        }

        // ─── CART HELPERS ─────────────────────────────────────────────────────

        private static List<CartItem> GetCart(HttpContext ctx)
        {
            string? json = ctx.Session.GetString("cart");
            if (string.IsNullOrEmpty(json)) return new List<CartItem>();
            return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
        }

        private static void SaveCart(HttpContext ctx, List<CartItem> cart) =>
            ctx.Session.SetString("cart", JsonSerializer.Serialize(cart));

        private static string RenderCartTable(List<CartItem> cart, bool withRemove)
        {
            if (cart.Count == 0)
                return "<p class='text-muted fst-italic'>No items added yet.</p>";

            var rows = new StringBuilder();
            decimal total = 0m;
            foreach (var item in cart)
            {
                decimal sub = item.UnitPrice * item.Quantity;
                total += sub;
                string removeBtn = withRemove
                    ? $@"<form method='post' action='/order/remove' class='d-inline'>
                           <input type='hidden' name='itemId' value='{item.ItemID}'>
                           <button type='submit' class='btn btn-sm btn-outline-danger'>Remove</button>
                         </form>"
                    : "";
                rows.Append($@"<tr>
                    <td>{H(item.Name)}</td>
                    <td class='text-center'>{item.Quantity}</td>
                    <td class='text-end'>${item.UnitPrice:F2}</td>
                    <td class='text-end fw-semibold'>${sub:F2}</td>
                    {(withRemove ? $"<td class='text-center'>{removeBtn}</td>" : "")}
                </tr>");
            }

            string removeHeader = withRemove ? "<th></th>" : "";
            string removeFoot   = withRemove ? "<td></td>" : "";

            return $@"<div class='table-responsive'>
  <table class='table table-bordered table-hover align-middle mb-0'>
    <thead class='table-warning'>
      <tr><th>Item</th><th class='text-center'>Qty</th><th class='text-end'>Unit Price</th><th class='text-end'>Subtotal</th>{removeHeader}</tr>
    </thead>
    <tbody>{rows}</tbody>
    <tfoot class='table-light'>
      <tr class='fw-bold'>
        <td colspan='3' class='text-end'>Order Total:</td>
        <td class='text-end'>${total:F2}</td>
        {removeFoot}
      </tr>
    </tfoot>
  </table>
</div>";
        }

        // ─── ROUTE HANDLERS ───────────────────────────────────────────────────

        // GET /  — Landing / hero page
        private static IResult HandleLanding(HttpContext ctx)
        {
            if (IsAuthenticated(ctx)) return Results.Redirect("/home");

            string body = @"
<div class='jj-hero min-vh-100 d-flex align-items-center py-5'>
  <div class='container text-center text-white'>
    <img src='/brand/logos/logo-primary-transparent.png' style='max-width:min(100%,480px);height:auto;display:block;margin:0 auto 1.5rem;' alt='JabberJuicy'>
    <p class='lead fs-3 mb-1'>Curiously delicious. Frabjously fresh.</p>
    <p class='fs-5 fst-italic mb-5' style='color:#fde68a;'>&ldquo;'Twas brillig, and the slithy toves...&rdquo;</p>
    <div class='row justify-content-center g-4 mb-5'>
      <div class='col-sm-10 col-md-4'>
        <div class='card shadow-lg h-100 border-0'>
          <div class='card-body text-dark p-4'>
            <h3 class='card-title fw-bold'>Returning Guest</h3>
            <p class='card-text text-muted'>Welcome back, brave adventurer. Sign in to place your order.</p>
            <a href='/login' class='btn btn-warning btn-lg w-100 fw-bold'>Login</a>
          </div>
        </div>
      </div>
      <div class='col-sm-10 col-md-4'>
        <div class='card shadow-lg h-100 border-0'>
          <div class='card-body text-dark p-4'>
            <h3 class='card-title fw-bold'>New to JabberJuicy?</h3>
            <p class='card-text text-muted'>Join our circle of frabjous juice lovers today.</p>
            <a href='/register' class='btn btn-success btn-lg w-100 fw-bold'>Create Account</a>
          </div>
        </div>
      </div>
    </div>
    <a href='/menu' class='btn btn-outline-light btn-lg'>View Our Menu</a>
  </div>
</div>";

            return Results.Content(Layout("Welcome", null, body), "text/html");
        }

        // GET /admin/login
        private static IResult HandleAdminLoginGet(HttpContext ctx)
        {
            if (IsAdminAuthenticated(ctx)) return Results.Redirect("/admin");

            string msg = ctx.Request.Query["msg"].ToString();
            string err = ctx.Request.Query["err"].ToString();

            string alert = (msg, err) switch
            {
                ("loggedout", _) => "<div class='alert alert-success'>Admin session closed.</div>",
                (_, "invalid")   => "<div class='alert alert-danger'>Invalid admin username or password.</div>",
                (_, "inactive")  => "<div class='alert alert-danger'>This admin account is inactive.</div>",
                (_, "config")    => "<div class='alert alert-warning'>Admin tables are not available yet. Run <code>admin_db_sql.txt</code> and your local <code>insert_sql.txt</code> first.</div>",
                _                => ""
            };

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-11 col-md-8 col-lg-6'>
      <div class='card shadow border-danger border-2'>
        <div class='card-body p-4 p-md-5'>
          <div class='text-center mb-4'>
            <span class='badge rounded-pill border border-danger bg-white px-3 py-2 fw-semibold mb-3'
                  style='color:#b91c1c;'>Demo Admin</span>
            <h1 class='jj-brand fw-bold mb-2' style='color:#ea580c;'>Admin Login</h1>
            <p class='text-muted mb-0'>
              Separate admin authentication has been selected for the long-term implementation.
            </p>
          </div>
          {alert}
          <form method='post' action='/admin/login' novalidate>
            <div class='mb-3'>
              <label class='form-label fw-semibold'>Username</label>
              <input class='form-control' name='username' maxlength='30'
                     autocomplete='username' autofocus required>
            </div>
            <div class='mb-3'>
              <label class='form-label fw-semibold'>Password</label>
              <input class='form-control' type='password' name='password'
                     autocomplete='current-password' required>
            </div>
            <button type='submit' class='btn btn-danger w-100 fw-semibold'>
              Login to Admin Dashboard
            </button>
          </form>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Admin Login", null, body), "text/html");
        }

        // POST /admin/login
        private static async Task<IResult> HandleAdminLoginPost(HttpContext ctx, ILogger<Program> logger)
        {
            var form = await ctx.Request.ReadFormAsync();
            string user = form["username"].ToString().Trim();
            string pass = form["password"].ToString();

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                return Results.Redirect("/admin/login?err=invalid");

            var cmd = new SqlCommand(@"
                SELECT AdminUserID, ADM_Username, ADM_PasswordHash, ADM_DisplayName,
                       ADM_Role, ADM_IsActive, ADM_IsGlobalAccess
                FROM AdminUser
                WHERE ADM_Username = @u");
            cmd.Parameters.AddWithValue("@u", user);

            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null) return Results.Redirect("/admin/login?err=config");
            if (dt.Rows.Count == 0) return Results.Redirect("/admin/login?err=invalid");

            var row = dt.Rows[0];
            if (!Convert.ToBoolean(row["ADM_IsActive"]))
                return Results.Redirect("/admin/login?err=inactive");

            string stored = row["ADM_PasswordHash"]?.ToString() ?? "";
            if (!VerifyPassword(pass, stored))
                return Results.Redirect("/admin/login?err=invalid");

            SignInAdminSession(ctx, row);

            var updateCmd = new SqlCommand(
                "UPDATE AdminUser SET ADM_LastLoginAt = GETDATE() WHERE AdminUserID = @id");
            updateCmd.Parameters.AddWithValue("@id", Convert.ToInt32(row["AdminUserID"]));
            await ExecSqlCommandAsync(updateCmd, logger);

            return Results.Redirect("/admin");
        }

        // GET /admin/logout
        private static IResult HandleAdminLogout(HttpContext ctx)
        {
            ClearAdminSession(ctx);
            return Results.Redirect("/admin/login?msg=loggedout");
        }

        // POST /admin/points-adjust
        private static async Task<IResult> HandleAdminPointsAdjust(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAdminAuthenticated(ctx)) return Results.Redirect("/admin/login");

            var form = await ctx.Request.ReadFormAsync();
            int customerId = int.TryParse(form["customerId"], out int cid) ? cid : 0;
            int delta      = int.TryParse(form["delta"],      out int d)   ? d   : 0;
            string reason  = form["reason"].ToString().Trim();
            string grain       = form["grain"].ToString()       is { Length: > 0 } g ? g : "day";
            string metricRange = form["metricRange"].ToString() is { Length: > 0 } m ? m : "day";
            int? locationId = int.TryParse(form["locationId"], out int loc) ? loc : null;
            string q = form["q"].ToString().Trim();

            string failUrl = BuildAdminUrl(grain, locationId, q, customerId == 0 ? null : (int?)customerId, null, metricRange);

            if (customerId == 0 || delta == 0)
                return Results.Redirect(failUrl + "&adjErr=invalid");

            // Atomically clamp balance to >= 0 and return the new value
            var balCmd = new SqlCommand(@"
                UPDATE Customer
                SET CUS_PointsBalance = CASE
                    WHEN CUS_PointsBalance + @delta < 0 THEN 0
                    ELSE CUS_PointsBalance + @delta
                END
                OUTPUT INSERTED.CUS_PointsBalance
                WHERE CustomerID = @cid");
            balCmd.Parameters.AddWithValue("@delta", delta);
            balCmd.Parameters.AddWithValue("@cid", customerId);
            var balDt = await FillDataTableViaCommandAsync(balCmd, logger);

            if (balDt == null || balDt.Rows.Count == 0)
                return Results.Redirect(failUrl + "&adjErr=fail");

            int newBalance = Convert.ToInt32(balDt.Rows[0][0]);
            int adminId = GetCurrentAdminId(ctx);
            string adminDisplay = GetCurrentAdminDisplayName(ctx) ?? GetCurrentAdminUsername(ctx) ?? "Admin";
            string noteText = string.IsNullOrWhiteSpace(reason)
                ? $"Admin adjustment by {adminDisplay}: delta {delta}"
                : $"Admin adjustment by {adminDisplay}: {reason}";

            var auditCmd = new SqlCommand(@"
                INSERT INTO AdminAuditLog
                    (AdminUserID, CustomerID, AAL_ActionType, AAL_EntityType, AAL_EntityID, AAL_Details, AAL_CreatedAt)
                VALUES (@adminId, @cid, 'POINTS_ADJUST', 'Customer', @cid, @details, GETDATE())");
            auditCmd.Parameters.AddWithValue("@adminId", adminId);
            auditCmd.Parameters.AddWithValue("@cid", customerId);
            auditCmd.Parameters.AddWithValue("@details", $"delta={delta}, newBalance={newBalance}, reason={reason}");
            await ExecSqlCommandAsync(auditCmd, logger);

            string returnUrl = BuildAdminUrl(grain, locationId, q, (int?)customerId, null, metricRange);
            return Results.Redirect(returnUrl + "&adjOk=1");
        }

        // GET /admin
        private static async Task<IResult> HandleAdminDashboard(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAdminAuthenticated(ctx)) return Results.Redirect("/admin/login");

            string adminDisplay = GetCurrentAdminDisplayName(ctx) ?? GetCurrentAdminUsername(ctx) ?? "Admin";
            string adminRole = GetCurrentAdminRole(ctx) ?? "Admin";
            bool isGlobal = GetCurrentAdminIsGlobalAccess(ctx);
            string grain = ctx.Request.Query["grain"].ToString().ToLowerInvariant() switch
            {
                "hour" => "hour",
                "week" => "week",
                _ => "day"
            };
            string metricRange = ctx.Request.Query["metricRange"].ToString().ToLowerInvariant() switch
            {
                "week" => "week",
                "month" => "month",
                _ => "day"
            };
            string q = ctx.Request.Query["q"].ToString().Trim();
            int? requestedLocationId = ParseNullableInt(ctx.Request.Query["locationId"]);
            int? selectedCustomerId = ParseNullableInt(ctx.Request.Query["customerId"]);
            int? selectedOrderId = ParseNullableInt(ctx.Request.Query["orderId"]);
            if (!selectedOrderId.HasValue && int.TryParse(q, out int searchedOrderId))
                selectedOrderId = searchedOrderId;

            var locationsDt = await GetAdminAccessibleLocationsAsync(GetCurrentAdminId(ctx), isGlobal, logger);
            if (locationsDt == null)
            {
                string setupBody = RenderAdminSetupBody(
                    adminDisplay,
                    "Admin database objects are not available or the query failed.",
                    "Apply admin_db_sql.txt and your local insert_sql.txt, then try the admin dashboard again.");
                return Results.Content(Layout("Admin Dashboard", null, setupBody), "text/html");
            }

            if (!isGlobal && locationsDt.Rows.Count == 0)
            {
                string deniedBody = RenderAdminSetupBody(
                    adminDisplay,
                    "This admin account has no assigned store access.",
                    "Add at least one AdminLocationAccess row or mark the account as global.");
                return Results.Content(Layout("Admin Dashboard", null, deniedBody), "text/html");
            }

            int? selectedLocationId = ResolveAdminLocationId(locationsDt, requestedLocationId, isGlobal);

            if (selectedOrderId.HasValue && !selectedCustomerId.HasValue)
                selectedCustomerId = await GetCustomerIdForAdminOrderAsync(selectedOrderId.Value, selectedLocationId, logger);

            int adminId = GetCurrentAdminId(ctx);
            string adjOk  = ctx.Request.Query["adjOk"].ToString();
            string adjErr = ctx.Request.Query["adjErr"].ToString();
            string adjAlert = (adjOk, adjErr) switch
            {
                ("1", _)       => "<div class='alert alert-success py-2 mb-3'>Points adjusted successfully.</div>",
                (_, "invalid") => "<div class='alert alert-danger py-2 mb-3'>Invalid adjustment — delta must be non-zero and customer must be selected.</div>",
                (_, "fail")    => "<div class='alert alert-danger py-2 mb-3'>Adjustment failed — please try again.</div>",
                _              => ""
            };

            // Run all independent dashboard queries in parallel
            var overviewTask       = GetAdminOverviewMetricsAsync(metricRange, selectedLocationId, logger);
            var searchTask         = SearchAdminCustomersAsync(q, adminId, isGlobal, logger);
            var customerTask       = selectedCustomerId.HasValue
                ? GetAdminCustomerDetailAsync(selectedCustomerId.Value, logger)
                : Task.FromResult<DataTable?>(null);
            var customerOrdersTask = selectedCustomerId.HasValue
                ? GetAdminCustomerOrdersAsync(selectedCustomerId.Value, logger)
                : Task.FromResult<DataTable?>(null);
            var customerPointsTask = selectedCustomerId.HasValue
                ? GetAdminCustomerPointsAsync(selectedCustomerId.Value, logger)
                : Task.FromResult<DataTable?>(null);
            var customerCasesTask  = selectedCustomerId.HasValue
                ? GetAdminCustomerCasesAsync(selectedCustomerId.Value, logger)
                : Task.FromResult<DataTable?>(null);
            var liveOrdersTask     = GetAdminLiveOrdersAsync(selectedLocationId, logger);
            var salesTrendTask     = GetAdminSalesTrendAsync(grain, selectedLocationId, logger);
            var activityTask       = GetAdminRecentActivityAsync(selectedLocationId, logger);
            var popularItemsTask   = GetAdminPopularItemsAsync(grain, selectedLocationId, logger);
            var paymentMixTask     = GetAdminPaymentMixAsync(grain, selectedLocationId, logger);
            var pointsMixTask      = GetAdminPointsMixAsync(grain, selectedLocationId, logger);
            var locationPerfTask   = GetAdminLocationPerformanceAsync(isGlobal, adminId, selectedLocationId, logger);

            await Task.WhenAll(overviewTask, searchTask, customerTask, customerOrdersTask,
                customerPointsTask, customerCasesTask, liveOrdersTask, salesTrendTask,
                activityTask, popularItemsTask, paymentMixTask, pointsMixTask, locationPerfTask);

            var overviewDt             = overviewTask.Result;
            var searchDt               = searchTask.Result;
            var selectedCustomerDt     = customerTask.Result;
            var selectedCustomerOrdersDt = customerOrdersTask.Result;
            var selectedCustomerPointsDt = customerPointsTask.Result;
            var selectedCustomerCasesDt  = customerCasesTask.Result;
            var liveOrdersDt           = liveOrdersTask.Result;
            var salesTrendDt           = salesTrendTask.Result;
            var activityDt             = activityTask.Result;
            var popularItemsDt         = popularItemsTask.Result;
            var paymentMixDt           = paymentMixTask.Result;
            var pointsMixDt            = pointsMixTask.Result;
            var locationPerfDt         = locationPerfTask.Result;

            string body = RenderAdminDashboardBody(
                adminDisplay,
                adminRole,
                isGlobal,
                grain,
                metricRange,
                q,
                selectedLocationId,
                selectedCustomerId,
                selectedOrderId,
                adjAlert,
                locationsDt,
                overviewDt,
                searchDt,
                selectedCustomerDt,
                selectedCustomerOrdersDt,
                selectedCustomerPointsDt,
                selectedCustomerCasesDt,
                liveOrdersDt,
                salesTrendDt,
                activityDt,
                popularItemsDt,
                paymentMixDt,
                pointsMixDt,
                locationPerfDt);

            return Results.Content(Layout("Admin Dashboard", null, body), "text/html");
        }

        // GET /login
        private static IResult HandleLoginGet(HttpContext ctx)
        {
            if (IsAuthenticated(ctx)) return Results.Redirect("/home");

            string msg = ctx.Request.Query["msg"].ToString();
            string err = ctx.Request.Query["err"].ToString();

            string alert = (msg, err) switch
            {
                ("registered", _) => "<div class='alert alert-success alert-dismissible fade show'><strong>Account created!</strong> Please log in. <button type='button' class='btn-close' data-bs-dismiss='alert'></button></div>",
                (_, "invalid")    => "<div class='alert alert-danger'>Invalid username or password. Please try again.</div>",
                _                 => ""
            };

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-10 col-md-6 col-lg-5'>
      <div class='card shadow'>
        <div class='card-header text-white fw-bold fs-5 jj-nav'>
          Login to JabberJuicy
        </div>
        <div class='card-body p-4'>
          {alert}
          <form method='post' action='/login' novalidate>
            <div class='mb-3'>
              <label for='username' class='form-label fw-semibold'>Username</label>
              <input type='text' id='username' name='username' class='form-control'
                     required maxlength='30' autocomplete='username' autofocus>
            </div>
            <div class='mb-4'>
              <label for='password' class='form-label fw-semibold'>Password</label>
              <input type='password' id='password' name='password' class='form-control'
                     required autocomplete='current-password'>
            </div>
            <button type='submit' class='btn btn-warning w-100 fw-bold btn-lg'>Login</button>
          </form>
          <hr>
          <p class='text-center mb-0 text-muted'>
            Don&rsquo;t have an account? <a href='/register'>Create one here</a>
          </p>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Login", null, body), "text/html");
        }

        // POST /login
        private static async Task<IResult> HandleLoginPost(HttpContext ctx, ILogger<Program> logger)
        {
            var form     = await ctx.Request.ReadFormAsync();
            string user  = form["username"].ToString().Trim();
            string pass  = form["password"].ToString();

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                return Results.Redirect("/login?err=invalid");

            var cmd = new SqlCommand(
                "SELECT CustomerID, CUS_Username, CUS_PasswordHash FROM Customer WHERE CUS_Username = @u");
            cmd.Parameters.AddWithValue("@u", user);

            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0)
                return Results.Redirect("/login?err=invalid");

            var row        = dt.Rows[0];
            string stored  = row["CUS_PasswordHash"]?.ToString() ?? "";

            if (!VerifyPassword(pass, stored))
                return Results.Redirect("/login?err=invalid");

            ctx.Session.SetString("uid",   row["CustomerID"].ToString()!);
            ctx.Session.SetString("uname", row["CUS_Username"].ToString()!);

            return Results.Redirect("/home");
        }

        // GET /register
        private static IResult HandleRegisterGet(HttpContext ctx)
        {
            if (IsAuthenticated(ctx)) return Results.Redirect("/home");

            string err = ctx.Request.Query["err"].ToString();
            string alert = err switch
            {
                "exists"  => "<div class='alert alert-danger'>That username is already taken. Please choose another.</div>",
                "missing" => "<div class='alert alert-danger'>First name, last name, username, and password are required.</div>",
                "fail"    => "<div class='alert alert-danger'>Could not create account. Please try again.</div>",
                _         => ""
            };

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-12 col-md-8 col-lg-7'>
      <div class='card shadow'>
        <div class='card-header text-white fw-bold fs-5' style='background-color:#16a34a;'>
          Create Your JabberJuicy Account
        </div>
        <div class='card-body p-4'>
          {alert}
          <form method='post' action='/register' novalidate>
            <div class='row g-3'>
              <div class='col-md-6'>
                <label for='firstName' class='form-label fw-semibold'>First Name <span class='text-danger'>*</span></label>
                <input type='text' id='firstName' name='firstName' class='form-control' required maxlength='30'>
              </div>
              <div class='col-md-6'>
                <label for='lastName' class='form-label fw-semibold'>Last Name <span class='text-danger'>*</span></label>
                <input type='text' id='lastName' name='lastName' class='form-control' required maxlength='30'>
              </div>
              <div class='col-md-6'>
                <label for='regUsername' class='form-label fw-semibold'>Username <span class='text-danger'>*</span></label>
                <input type='text' id='regUsername' name='username' class='form-control' required maxlength='30' autocomplete='username'>
              </div>
              <div class='col-md-6'>
                <label for='regPassword' class='form-label fw-semibold'>Password <span class='text-danger'>*</span></label>
                <input type='password' id='regPassword' name='password' class='form-control' required autocomplete='new-password'>
              </div>
              <div class='col-12'>
                <label for='email' class='form-label fw-semibold'>Email</label>
                <input type='email' id='email' name='email' class='form-control' maxlength='50' autocomplete='email'>
              </div>
              <div class='col-md-6'>
                <label for='phone' class='form-label fw-semibold'>Phone</label>
                <input type='tel' id='phone' name='phone' class='form-control' maxlength='10' placeholder='10 digits, no dashes'>
              </div>
              <div class='col-12'>
                <label for='address' class='form-label fw-semibold'>Address</label>
                <input type='text' id='address' name='address' class='form-control' maxlength='100' autocomplete='street-address'>
              </div>
              <div class='col-md-5'>
                <label for='city' class='form-label fw-semibold'>City</label>
                <input type='text' id='city' name='city' class='form-control' maxlength='50' autocomplete='address-level2'>
              </div>
              <div class='col-md-3'>
                <label for='state' class='form-label fw-semibold'>State</label>
                <input type='text' id='state' name='state' class='form-control' maxlength='2' placeholder='e.g. TX' autocomplete='address-level1'>
              </div>
              <div class='col-md-4'>
                <label for='zip' class='form-label fw-semibold'>Zip Code</label>
                <input type='text' id='zip' name='zip' class='form-control' maxlength='5' placeholder='5 digits' autocomplete='postal-code'>
              </div>
              <div class='col-12 mt-2'>
                <button type='submit' class='btn btn-success btn-lg w-100 fw-bold'>Create Account</button>
              </div>
            </div>
          </form>
          <hr>
          <p class='text-center mb-0 text-muted'>Already have an account? <a href='/login'>Login here</a></p>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Create Account", null, body), "text/html");
        }

        // POST /register
        private static async Task<IResult> HandleRegisterPost(HttpContext ctx, ILogger<Program> logger)
        {
            var form       = await ctx.Request.ReadFormAsync();
            string first   = form["firstName"].ToString().Trim();
            string last    = form["lastName"].ToString().Trim();
            string user    = form["username"].ToString().Trim();
            string pass    = form["password"].ToString();
            string email   = form["email"].ToString().Trim();
            string phone   = form["phone"].ToString().Trim();
            string address = form["address"].ToString().Trim();
            string city    = form["city"].ToString().Trim();
            string state   = form["state"].ToString().Trim().ToUpper();
            string zip     = form["zip"].ToString().Trim();

            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last) ||
                string.IsNullOrEmpty(user)  || string.IsNullOrEmpty(pass))
                return Results.Redirect("/register?err=missing");

            if (await UsernameExistsAsync(user, logger))
                return Results.Redirect("/register?err=exists");

            string hash = HashPassword(pass);

            var insertCmd = new SqlCommand(@"
                INSERT INTO Customer
                    (CUS_FirstName, CUS_LastName, CUS_Username, CUS_PasswordHash,
                     CUS_Email, CUS_Phone, CUS_Address, CUS_City, CUS_State, CUS_ZipCode)
                VALUES
                    (@first, @last, @user, @hash,
                     @email, @phone, @address, @city, @state, @zip)");
            insertCmd.Parameters.AddWithValue("@first",   first);
            insertCmd.Parameters.AddWithValue("@last",    last);
            insertCmd.Parameters.AddWithValue("@user",    user);
            insertCmd.Parameters.AddWithValue("@hash",    hash);
            insertCmd.Parameters.AddWithValue("@email",   string.IsNullOrEmpty(email)   ? DBNull.Value : (object)email);
            insertCmd.Parameters.AddWithValue("@phone",   string.IsNullOrEmpty(phone)   ? DBNull.Value : (object)phone);
            insertCmd.Parameters.AddWithValue("@address", string.IsNullOrEmpty(address) ? DBNull.Value : (object)address);
            insertCmd.Parameters.AddWithValue("@city",    string.IsNullOrEmpty(city)    ? DBNull.Value : (object)city);
            insertCmd.Parameters.AddWithValue("@state",   string.IsNullOrEmpty(state)   ? DBNull.Value : (object)state);
            insertCmd.Parameters.AddWithValue("@zip",     string.IsNullOrEmpty(zip)     ? DBNull.Value : (object)zip);

            bool ok = await ExecSqlCommandAsync(insertCmd, logger);
            if (!ok) return Results.Redirect("/register?err=fail");

            return Results.Redirect("/login?msg=registered");
        }

        // GET /logout
        private static IResult HandleLogout(HttpContext ctx)
        {
            ctx.Session.Clear();
            return Results.Redirect("/");
        }

        // GET /home  — Main menu (requires auth)
        private static async Task<IResult> HandleHome(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username      = GetCurrentUsername(ctx)!;
            int    custId        = GetCurrentUserId(ctx);
            int    pointsBal     = await GetPointsBalanceAsync(custId, logger);
            int    pendingCount  = await GetPendingOrderCountAsync(custId, logger);

            string body = $@"
<div class='container py-5'>
  <div class='text-center mb-5'>
    <h1 class='jj-brand display-4 fw-bold' style='color:#ea580c;'>Welcome, {H(username)}!</h1>
    <p class='lead text-muted fst-italic'>What shall the vorpal blade snicker-snack today?</p>
    <img src='/brand/illustrations/mascot-dragon.png' height='180' style='width:auto;display:block;margin:0 auto 1rem;' alt='' class='d-none d-md-block'>
  </div>
  <div class='row g-4 justify-content-center'>
    <div class='col-sm-10 col-md-4'>
      <div class='card shadow h-100 border-warning border-2'>
        <div class='card-body text-center p-4 d-flex flex-column'>
          <h3 class='card-title fw-bold'>New Order</h3>
          <p class='card-text text-muted flex-grow-1'>
            Build your frabjous order, one slithy sip at a time.
            Mix and match our brillig blends!
          </p>
          <a href='/order' class='btn btn-warning btn-lg w-100 fw-bold'>Start Order</a>
        </div>
      </div>
    </div>
    <div class='col-sm-10 col-md-4'>
      <div class='card shadow h-100 border-success border-2'>
        <div class='card-body text-center p-4 d-flex flex-column'>
          <h3 class='card-title fw-bold'>View Menu</h3>
          <p class='card-text text-muted flex-grow-1'>
            Explore our full catalog of brillig blends, borogove brews,
            and frabjous specials.
          </p>
          <a href='/menu' class='btn btn-success btn-lg w-100 fw-bold'>See Menu</a>
        </div>
      </div>
    </div>
    <div class='col-sm-10 col-md-4'>
      <div class='card shadow h-100 border-primary border-2'>
        <div class='card-body text-center p-4 d-flex flex-column'>
          <h3 class='card-title fw-bold'>Order History</h3>
          <p class='card-text text-muted flex-grow-1'>
            Review your past galumphing adventures in juice. Relive
            every frabjous sip.
          </p>
          <a href='/history' class='btn btn-primary btn-lg w-100 fw-bold'>My Orders</a>
        </div>
      </div>
    </div>
    <div class='col-sm-10 col-md-4'>
      <div class='card shadow h-100 border-2' style='border-color:#fbbf24 !important;'>
        <div class='card-body text-center p-4 d-flex flex-column'>
          <h3 class='card-title fw-bold'>&#11088; JabberWonk Points</h3>
          <div class='display-4 fw-bold my-3' style='color:#ea580c;'>{pointsBal:N0}</div>
          <p class='card-text text-muted flex-grow-1'>
            Earn {JWP_PointsPerVisit} pts per visit + {JWP_PointsPerDollar} pts per $1 spent.<br>
            Redeem {JWP_PointsPerRedempt} pts = $1.00 off your next order.
          </p>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Home", username, body, pointsBal, pendingCount), "text/html");
        }

        // GET /menu  — Public item catalog
        private static async Task<IResult> HandleMenu(HttpContext ctx, ILogger<Program> logger)
        {
            string? username = GetCurrentUsername(ctx);

            var dt = await FillDataTableViaSqlAsync(
                "SELECT ItemID, ITM_ItemName, ITM_Description, ITM_Category, ITM_UnitPrice, ITM_StockQty " +
                "FROM Item ORDER BY ITM_Category, ITM_ItemName", logger);

            var cards = new StringBuilder();
            if (dt == null || dt.Rows.Count == 0)
            {
                cards.Append("<div class='col-12 text-center py-5'><p class='text-muted fst-italic fs-5'>The menu is being updated. Check back soon!</p></div>");
            }
            else
            {
                foreach (DataRow row in dt.Rows)
                {
                    string name  = H(row["ITM_ItemName"]?.ToString() ?? "");
                    string desc  = H(row["ITM_Description"]?.ToString() ?? "");
                    string cat   = H(row["ITM_Category"]?.ToString() ?? "");
                    decimal price = row["ITM_UnitPrice"] == DBNull.Value ? 0m : Convert.ToDecimal(row["ITM_UnitPrice"]);
                    int stock     = row["ITM_StockQty"]  == DBNull.Value ? 99 : Convert.ToInt32(row["ITM_StockQty"]);

                    string catLower = cat.ToLower();
                    string badgeClass = catLower switch
                    {
                        "smoothie" => "bg-success",
                        "juice"    => "bg-warning text-dark",
                        "special"  => "bg-primary",
                        _          => "bg-secondary"
                    };

                    string stockBadge = stock == 0
                        ? "<span class='badge bg-danger'>Out of Stock</span>"
                        : "";

                    string orderBtn = username != null && stock > 0
                        ? "<a href='/order' class='btn btn-sm btn-warning fw-semibold'>Order Now</a>"
                        : "";

                    cards.Append($@"
<div class='col'>
  <div class='card shadow-sm h-100'>
    <div class='card-body'>
      <div class='d-flex justify-content-between align-items-start mb-2'>
        <h5 class='card-title fw-bold mb-0'>{name}</h5>
        <span class='badge {badgeClass} ms-2'>{cat}</span>
      </div>
      <p class='card-text text-muted small'>{desc}</p>
    </div>
    <div class='card-footer d-flex justify-content-between align-items-center'>
      <span class='fw-bold fs-5' style='color:#ea580c;'>${price:F2}</span>
      <div class='d-flex gap-2 align-items-center'>{stockBadge}{orderBtn}</div>
    </div>
  </div>
</div>");
                }
            }

            string loginPrompt = username == null
                ? @"<div class='text-center mt-4'>
                      <p class='text-muted'>Ready to order? Login or create an account.</p>
                      <a href='/login' class='btn btn-warning btn-lg me-2'>Login</a>
                      <a href='/register' class='btn btn-outline-success btn-lg'>Register</a>
                    </div>"
                : "";

            string body = $@"
<div class='container py-5'>
  <div class='text-center mb-4'>
    <h1 class='jj-brand display-5 fw-bold' style='color:#ea580c;'>Our Menu</h1>
    <p class='lead text-muted fst-italic'>&ldquo;All mimsy were the borogoves, and the mome raths outgrabe.&rdquo;</p>
  </div>
  <div class='row row-cols-1 row-cols-sm-2 row-cols-md-3 g-4'>
    {cards}
  </div>
  {loginPrompt}
</div>";

            return Results.Content(Layout("Menu", username, body), "text/html");
        }

        // GET /order  — Build order / cart (requires auth)
        private static async Task<IResult> HandleOrderGet(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username     = GetCurrentUsername(ctx)!;
            int    pendingCount = await GetPendingOrderCountAsync(GetCurrentUserId(ctx), logger);

            var itemsDt = await FillDataTableViaSqlAsync(
                "SELECT ItemID, ITM_ItemName, ITM_UnitPrice FROM Item " +
                "WHERE ITM_StockQty IS NULL OR ITM_StockQty > 0 " +
                "ORDER BY ITM_Category, ITM_ItemName", logger);

            var options = new StringBuilder("<option value=''>-- Choose a drink --</option>");
            if (itemsDt != null)
                foreach (DataRow row in itemsDt.Rows)
                {
                    int    id    = Convert.ToInt32(row["ItemID"]);
                    string name  = H(row["ITM_ItemName"]?.ToString() ?? "");
                    decimal price = Convert.ToDecimal(row["ITM_UnitPrice"]);
                    options.Append($"<option value='{id}'>{name} &mdash; ${price:F2}</option>");
                }

            var cart      = GetCart(ctx);
            string cartHtml = RenderCartTable(cart, withRemove: true);
            bool   hasItems = cart.Count > 0;

            string err   = ctx.Request.Query["err"].ToString();
            string alert = err == "noitem" ? "<div class='alert alert-warning'>Please select an item from the dropdown.</div>" : "";

            string body = $@"
<div class='container py-4'>
  <h1 class='jj-brand fw-bold mb-4' style='color:#ea580c;'>Build Your Order</h1>
  {alert}
  <div class='row g-4'>
    <div class='col-md-5'>
      <div class='card shadow'>
        <div class='card-header text-white fw-bold jj-nav'>Add Items</div>
        <div class='card-body'>
          <form method='post' action='/order/add'>
            <div class='mb-3'>
              <label for='itemSelect' class='form-label fw-semibold'>Select a Drink</label>
              <select id='itemSelect' name='itemId' class='form-select' required>
                {options}
              </select>
            </div>
            <div class='mb-3'>
              <label for='qtyInput' class='form-label fw-semibold'>Quantity</label>
              <input type='number' id='qtyInput' name='quantity'
                     class='form-control' value='1' min='1' max='99'>
            </div>
            <button type='submit' class='btn btn-warning w-100 fw-semibold'>
              Add to Order
            </button>
          </form>
          <hr>
          <form method='post' action='/order/clear'>
            <button type='submit' class='btn btn-outline-secondary w-100 btn-sm'
                    {(hasItems ? "" : "disabled")}>
              Clear Cart
            </button>
          </form>
        </div>
      </div>
    </div>
    <div class='col-md-7'>
      <div class='card shadow'>
        <div class='card-header text-white fw-bold' style='background-color:#16a34a;'>Your Cart</div>
        <div class='card-body'>{cartHtml}</div>
        <div class='card-footer text-end'>
          <a href='/checkout'
             class='btn btn-success btn-lg fw-bold {(hasItems ? "" : "disabled")}'
             aria-disabled='{(hasItems ? "false" : "true")}'>
            Proceed to Checkout &rarr;
          </a>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("New Order", username, body, null, pendingCount), "text/html");
        }

        // POST /order/add  — Add item to cart
        private static async Task<IResult> HandleOrderAdd(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");

            var form = await ctx.Request.ReadFormAsync();
            if (!int.TryParse(form["itemId"].ToString(), out int itemId) || itemId <= 0)
                return Results.Redirect("/order?err=noitem");
            if (!int.TryParse(form["quantity"].ToString(), out int qty) || qty < 1)
                qty = 1;

            var cmd = new SqlCommand(
                "SELECT ItemID, ITM_ItemName, ITM_UnitPrice FROM Item WHERE ItemID = @id");
            cmd.Parameters.AddWithValue("@id", itemId);
            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0)
                return Results.Redirect("/order?err=noitem");

            var row   = dt.Rows[0];
            string nm = row["ITM_ItemName"]?.ToString() ?? "";
            decimal pr = Convert.ToDecimal(row["ITM_UnitPrice"]);

            var cart     = GetCart(ctx);
            var existing = cart.FirstOrDefault(c => c.ItemID == itemId);
            if (existing != null)
            {
                cart.Remove(existing);
                cart.Add(existing with { Quantity = existing.Quantity + qty });
            }
            else
            {
                cart.Add(new CartItem(itemId, nm, pr, qty));
            }
            SaveCart(ctx, cart);

            return Results.Redirect("/order");
        }

        // POST /order/remove  — Remove item from cart
        private static async Task<IResult> HandleOrderRemove(HttpContext ctx)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");

            var form = await ctx.Request.ReadFormAsync();
            if (int.TryParse(form["itemId"].ToString(), out int itemId))
            {
                var cart = GetCart(ctx);
                cart.RemoveAll(c => c.ItemID == itemId);
                SaveCart(ctx, cart);
            }

            return Results.Redirect("/order");
        }

        // POST /order/clear  — Empty cart
        private static async Task<IResult> HandleOrderClear(HttpContext ctx)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            await ctx.Request.ReadFormAsync();
            ctx.Session.Remove("cart");
            return Results.Redirect("/order");
        }

        // GET /checkout  — Review order before submitting
        private static async Task<IResult> HandleCheckoutGet(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username = GetCurrentUsername(ctx)!;

            var cart = GetCart(ctx);
            if (cart.Count == 0) return Results.Redirect("/order");

            var locDt = await FillDataTableViaSqlAsync(
                "SELECT MIN(LocationID) AS LocationID, LOC_StoreName, LOC_City, LOC_State " +
                "FROM Location GROUP BY LOC_StoreName, LOC_City, LOC_State ORDER BY LOC_StoreName",
                logger);
            var payDt = await FillDataTableViaSqlAsync(
                "SELECT PaymentTypeID, PAY_TypeName FROM PaymentType ORDER BY PAY_TypeName",
                logger);

            var locOptions = new StringBuilder("<option value=''>-- Select a location --</option>");
            if (locDt != null)
                foreach (DataRow row in locDt.Rows)
                    locOptions.Append($"<option value='{row["LocationID"]}'>" +
                        $"{H(row["LOC_StoreName"]?.ToString())} &mdash; " +
                        $"{H(row["LOC_City"]?.ToString())}, {H(row["LOC_State"]?.ToString())}</option>");

            string cartHtml = RenderCartTable(cart, withRemove: false);
            decimal total   = cart.Sum(i => i.UnitPrice * i.Quantity);

            int  custId        = GetCurrentUserId(ctx);
            int  pointsBal     = await GetPointsBalanceAsync(custId, logger);
            int  jwpId         = await GetJabberWonkPaymentTypeIdAsync(logger);
            int  pointsNeeded  = (int)Math.Ceiling(total * JWP_PointsPerRedempt);
            bool canPayWithPts = pointsBal >= pointsNeeded && pointsNeeded > 0;

            string bannerClass  = canPayWithPts ? "alert-success" : "alert-secondary";
            string bannerMsg    = canPayWithPts
                ? $"You can cover this {total:C} order with {pointsNeeded:N0} points!"
                : $"You need {pointsNeeded:N0} pts to pay with points. Keep earning!";
            string pointsBanner = $"<div class='alert {bannerClass} py-2 small mb-3'>" +
                $"<strong>&#11088; Balance: {pointsBal:N0} pts</strong><br>{bannerMsg}</div>";

            var payOptions = new StringBuilder("<option value=''>-- Select payment type --</option>");
            if (payDt != null)
                foreach (DataRow row in payDt.Rows)
                {
                    int    payId   = Convert.ToInt32(row["PaymentTypeID"]);
                    string payName = H(row["PAY_TypeName"]?.ToString());
                    if (payId == jwpId)
                    {
                        string label    = canPayWithPts
                            ? $"JabberWonk Points ({pointsBal:N0} pts — enough!)"
                            : $"JabberWonk Points (need {pointsNeeded:N0}, have {pointsBal:N0})";
                        string disabled = canPayWithPts ? "" : "disabled";
                        payOptions.Append($"<option value='{payId}' {disabled}>{label}</option>");
                    }
                    else
                    {
                        payOptions.Append($"<option value='{payId}'>{payName}</option>");
                    }
                }

            string err   = ctx.Request.Query["err"].ToString();
            string alert = err switch
            {
                "noloc"    => "<div class='alert alert-danger'>Please select a location.</div>",
                "nopay"    => "<div class='alert alert-danger'>Please select a payment method.</div>",
                "fail"     => "<div class='alert alert-danger'>Could not save order. Please try again.</div>",
                "nopoints" => "<div class='alert alert-danger'>Not enough JabberWonk Points to cover this order.</div>",
                _          => ""
            };

            string body = $@"
<div class='container py-4'>
  <h1 class='jj-brand fw-bold mb-4' style='color:#ea580c;'>Checkout</h1>
  {alert}
  <div class='row g-4'>
    <div class='col-md-7'>
      <div class='card shadow mb-3'>
        <div class='card-header fw-bold text-white jj-nav'>Order Summary</div>
        <div class='card-body p-3'>{cartHtml}</div>
      </div>
      <a href='/order' class='btn btn-outline-secondary'>&larr; Edit Order</a>
    </div>
    <div class='col-md-5'>
      <div class='card shadow'>
        <div class='card-header fw-bold text-white' style='background-color:#16a34a;'>
          Complete Transaction
        </div>
        <div class='card-body'>
          <form method='post' action='/checkout' novalidate>
            <div class='mb-3'>
              <label for='locationId' class='form-label fw-semibold'>
                Pick-up Location <span class='text-danger'>*</span>
              </label>
              <select id='locationId' name='locationId' class='form-select' required>
                {locOptions}
              </select>
            </div>
            <div class='mb-3'>
              <label for='paymentTypeId' class='form-label fw-semibold'>
                Payment Method <span class='text-danger'>*</span>
              </label>
              <select id='paymentTypeId' name='paymentTypeId' class='form-select' required>
                {payOptions}
              </select>
            </div>
            {pointsBanner}
            <div class='mb-3'>
              <label for='notes' class='form-label fw-semibold'>Special Notes</label>
              <textarea id='notes' name='notes' class='form-control' rows='3'
                        maxlength='300'
                        placeholder='Allergies, substitutions, special requests...'></textarea>
            </div>
            <div class='alert alert-warning d-flex justify-content-between align-items-center py-2 mb-3'>
              <strong>Total Due:</strong>
              <span class='fs-4 fw-bold'>${total:F2}</span>
            </div>
            <button type='submit' class='btn btn-success btn-lg w-100 fw-bold'>
              Complete Transaction
            </button>
          </form>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Checkout", username, body), "text/html");
        }

        // POST /checkout  — Submit order to database
        private static async Task<IResult> HandleCheckoutPost(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            int customerId = GetCurrentUserId(ctx);

            var form = await ctx.Request.ReadFormAsync();
            if (!int.TryParse(form["locationId"].ToString(), out int locationId) || locationId <= 0)
                return Results.Redirect("/checkout?err=noloc");
            if (!int.TryParse(form["paymentTypeId"].ToString(), out int paymentTypeId) || paymentTypeId <= 0)
                return Results.Redirect("/checkout?err=nopay");

            string notes = form["notes"].ToString().Trim();
            var    cart  = GetCart(ctx);
            if (cart.Count == 0) return Results.Redirect("/order");

            decimal total = cart.Sum(i => i.UnitPrice * i.Quantity);

            // INSERT Order — use OUTPUT to get the new OrderID back
            var orderCmd = new SqlCommand(@"
                INSERT INTO [Order]
                    (ORD_Status, ORD_Notes, ORD_TotalAmount, CustomerID, LocationID, PaymentTypeID)
                OUTPUT INSERTED.OrderID
                VALUES ('Pending', @notes, @total, @custId, @locId, @payId)");
            orderCmd.Parameters.AddWithValue("@notes",  string.IsNullOrEmpty(notes) ? DBNull.Value : (object)notes);
            orderCmd.Parameters.AddWithValue("@total",  total);
            orderCmd.Parameters.AddWithValue("@custId", customerId);
            orderCmd.Parameters.AddWithValue("@locId",  locationId);
            orderCmd.Parameters.AddWithValue("@payId",  paymentTypeId);

            var orderDt = await FillDataTableViaCommandAsync(orderCmd, logger);
            if (orderDt == null || orderDt.Rows.Count == 0)
                return Results.Redirect("/checkout?err=fail");

            int orderId = Convert.ToInt32(orderDt.Rows[0]["OrderID"]);

            // INSERT one OrderItem row per cart item
            foreach (var item in cart)
            {
                var itemCmd = new SqlCommand(@"
                    INSERT INTO OrderItem (OrderID, ItemID, ORI_Quantity, ORI_UnitPrice)
                    VALUES (@orderId, @itemId, @qty, @price)");
                itemCmd.Parameters.AddWithValue("@orderId", orderId);
                itemCmd.Parameters.AddWithValue("@itemId",  item.ItemID);
                itemCmd.Parameters.AddWithValue("@qty",     item.Quantity);
                itemCmd.Parameters.AddWithValue("@price",   item.UnitPrice);
                await ExecSqlCommandAsync(itemCmd, logger);
            }

            // ─── JabberWonk Points ────────────────────────────────────────────
            int  jwpPayId     = await GetJabberWonkPaymentTypeIdAsync(logger);
            bool isPointsPay  = paymentTypeId == jwpPayId && jwpPayId > 0;
            int  currentBal   = await GetPointsBalanceAsync(customerId, logger);
            int  pointsDelta;
            int  newBalance;

            if (isPointsPay)
            {
                int pointsNeeded = (int)Math.Ceiling(total * JWP_PointsPerRedempt);
                if (currentBal < pointsNeeded)
                    return Results.Redirect("/checkout?err=nopoints");
                pointsDelta = -pointsNeeded;
                newBalance  = currentBal - pointsNeeded;
            }
            else
            {
                int earned  = Math.Min(
                    JWP_PointsPerVisit + ((int)Math.Floor(total) * JWP_PointsPerDollar),
                    JWP_MaxEarnPerVisit);
                pointsDelta = earned;
                newBalance  = currentBal + earned;
            }

            var updateCmd = new SqlCommand(
                "UPDATE Customer SET CUS_PointsBalance = @bal WHERE CustomerID = @cid");
            updateCmd.Parameters.AddWithValue("@bal", newBalance);
            updateCmd.Parameters.AddWithValue("@cid", customerId);
            await ExecSqlCommandAsync(updateCmd, logger);

            string txType = isPointsPay ? "REDEEM" : "EARN";
            var txCmd = new SqlCommand(@"
                INSERT INTO JabberWonkTransaction
                    (JWT_PointsDelta, JWT_TransactionType, JWT_BalanceAfter, JWT_Notes, CustomerID, OrderID)
                VALUES (@delta, @type, @after, @notes, @cid, @oid)");
            txCmd.Parameters.AddWithValue("@delta", pointsDelta);
            txCmd.Parameters.AddWithValue("@type",  txType);
            txCmd.Parameters.AddWithValue("@after", newBalance);
            txCmd.Parameters.AddWithValue("@notes", $"{txType} on Order #{orderId}");
            txCmd.Parameters.AddWithValue("@cid",   customerId);
            txCmd.Parameters.AddWithValue("@oid",   orderId);
            await ExecSqlCommandAsync(txCmd, logger);

            // Store order details in session for receipt page, clear cart
            ctx.Session.SetString("lastOrderId",      orderId.ToString());
            ctx.Session.SetString("lastOrderTotal",   total.ToString("F2"));
            ctx.Session.SetString("lastPointsDelta",  pointsDelta.ToString());
            ctx.Session.SetString("lastPointsBalance", newBalance.ToString());
            ctx.Session.Remove("cart");

            return Results.Redirect("/receipt");
        }

        // GET /receipt  — Order confirmation
        private static async Task<IResult> HandleReceipt(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username     = GetCurrentUsername(ctx)!;
            int    pendingCount = await GetPendingOrderCountAsync(GetCurrentUserId(ctx), logger);

            string? orderIdStr  = ctx.Session.GetString("lastOrderId");
            if (string.IsNullOrEmpty(orderIdStr)) return Results.Redirect("/history");

            int    orderId      = int.Parse(orderIdStr);
            string sessionTotal = ctx.Session.GetString("lastOrderTotal") ?? "0.00";

            string? ptsDeltaStr = ctx.Session.GetString("lastPointsDelta");
            string? ptsBal      = ctx.Session.GetString("lastPointsBalance");
            int     ptsDelta    = int.TryParse(ptsDeltaStr, out int d) ? d : 0;
            int     newBal      = int.TryParse(ptsBal,      out int b) ? b : 0;
            ctx.Session.Remove("lastPointsDelta");
            ctx.Session.Remove("lastPointsBalance");

            // Fetch order header
            var orderCmd = new SqlCommand(@"
                SELECT o.OrderID, o.ORD_OrderDate, o.ORD_Status, o.ORD_Notes, o.ORD_TotalAmount,
                       l.LOC_StoreName, l.LOC_City, l.LOC_State,
                       p.PAY_TypeName
                FROM [Order] o
                JOIN Location l    ON o.LocationID    = l.LocationID
                JOIN PaymentType p ON o.PaymentTypeID = p.PaymentTypeID
                WHERE o.OrderID = @id");
            orderCmd.Parameters.AddWithValue("@id", orderId);
            var orderDt = await FillDataTableViaCommandAsync(orderCmd, logger);

            // Fetch order line items
            var itemsCmd = new SqlCommand(@"
                SELECT i.ITM_ItemName, oi.ORI_Quantity, oi.ORI_UnitPrice, oi.ORI_Subtotal
                FROM OrderItem oi
                JOIN Item i ON oi.ItemID = i.ItemID
                WHERE oi.OrderID = @id
                ORDER BY i.ITM_ItemName");
            itemsCmd.Parameters.AddWithValue("@id", orderId);
            var itemsDt = await FillDataTableViaCommandAsync(itemsCmd, logger);

            string storeName = "—", payType = "—", orderDate = "—", displayTotal = sessionTotal;
            string orderNotes = "";

            if (orderDt != null && orderDt.Rows.Count > 0)
            {
                var row   = orderDt.Rows[0];
                storeName = $"{H(row["LOC_StoreName"]?.ToString())} &mdash; " +
                            $"{H(row["LOC_City"]?.ToString())}, {H(row["LOC_State"]?.ToString())}";
                payType   = H(row["PAY_TypeName"]?.ToString());
                orderDate = row["ORD_OrderDate"] == DBNull.Value
                    ? "—" : Convert.ToDateTime(row["ORD_OrderDate"]).ToString("f");
                if (row["ORD_TotalAmount"] != DBNull.Value)
                    displayTotal = $"{Convert.ToDecimal(row["ORD_TotalAmount"]):F2}";
                if (row["ORD_Notes"] != DBNull.Value && !string.IsNullOrWhiteSpace(row["ORD_Notes"]?.ToString()))
                    orderNotes = $"<p class='text-muted small'><strong>Notes:</strong> {H(row["ORD_Notes"]?.ToString())}</p>";
            }

            var itemRows = new StringBuilder();
            if (itemsDt != null)
                foreach (DataRow row in itemsDt.Rows)
                {
                    string name  = H(row["ITM_ItemName"]?.ToString() ?? "");
                    int    qty   = Convert.ToInt32(row["ORI_Quantity"]);
                    decimal up   = Convert.ToDecimal(row["ORI_UnitPrice"]);
                    decimal sub  = Convert.ToDecimal(row["ORI_Subtotal"]);
                    itemRows.Append($"<tr><td>{name}</td><td class='text-center'>{qty}</td><td class='text-end'>${up:F2}</td><td class='text-end fw-semibold'>${sub:F2}</td></tr>");
                }

            string pointsSummary = ptsDeltaStr == null ? "" : ptsDelta < 0
                ? $"<div class='alert alert-warning mt-3'>" +
                  $"<strong>&#11088; Redeemed {Math.Abs(ptsDelta):N0} JabberWonk Points</strong><br>" +
                  $"<span class='text-muted small'>Remaining balance: {newBal:N0} pts</span></div>"
                : $"<div class='alert alert-success mt-3'>" +
                  $"<strong>&#11088; +{ptsDelta:N0} JabberWonk Points Earned!</strong><br>" +
                  $"<span class='text-muted small'>New balance: {newBal:N0} pts</span></div>";

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-12 col-md-8 col-lg-7'>
      <div class='card shadow border-success border-2'>
        <div class='card-header text-white fw-bold fs-5 text-center' style='background-color:#16a34a;'>
          Order Confirmed &mdash; O frabjous day! Callooh! Callay!
        </div>
        <div class='card-body p-4'>
          <div class='row mb-3 text-center text-md-start'>
            <div class='col-6'><strong>Order #:</strong> {orderId}</div>
            <div class='col-6 text-md-end'><strong>Date:</strong> {orderDate}</div>
          </div>
          <div class='row mb-3 text-center text-md-start'>
            <div class='col-md-7'><strong>Location:</strong> {storeName}</div>
            <div class='col-md-5 text-md-end'><strong>Payment:</strong> {payType}</div>
          </div>
          {orderNotes}
          <hr>
          <table class='table table-bordered'>
            <thead class='table-warning'>
              <tr>
                <th>Item</th>
                <th class='text-center'>Qty</th>
                <th class='text-end'>Unit Price</th>
                <th class='text-end'>Subtotal</th>
              </tr>
            </thead>
            <tbody>{itemRows}</tbody>
            <tfoot class='table-light fw-bold'>
              <tr>
                <td colspan='3' class='text-end'>Total Paid:</td>
                <td class='text-end'>${displayTotal}</td>
              </tr>
            </tfoot>
          </table>
          {pointsSummary}
          <p class='text-muted text-center fst-italic mt-2'>
            He chortled in his joy &mdash; your order is on its way!
          </p>
        </div>
        <div class='card-footer d-flex flex-wrap gap-2 justify-content-center py-3'>
          <a href='/pickup/{orderId}' class='btn btn-success fw-bold'>&#x1F7E2; Confirm Pickup</a>
          <a href='/order'            class='btn btn-warning fw-semibold'>Place Another Order</a>
          <a href='/history'          class='btn btn-outline-primary'>View Order History</a>
          <a href='/home'             class='btn btn-outline-secondary'>Main Menu</a>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Receipt", username, body, null, pendingCount), "text/html");
        }

        // GET /history  — Past orders for logged-in customer
        private static async Task<IResult> HandleHistory(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username     = GetCurrentUsername(ctx)!;
            int    custId       = GetCurrentUserId(ctx);
            int    pendingCount = await GetPendingOrderCountAsync(custId, logger);

            var cmd = new SqlCommand(@"
                SELECT
                    o.OrderID,
                    o.ORD_OrderDate,
                    o.ORD_Status,
                    o.ORD_TotalAmount,
                    l.LOC_StoreName,
                    l.LOC_City,
                    p.PAY_TypeName,
                    STRING_AGG(i.ITM_ItemName, ', ') AS Items
                FROM [Order] o
                JOIN Location    l  ON o.LocationID    = l.LocationID
                JOIN PaymentType p  ON o.PaymentTypeID = p.PaymentTypeID
                JOIN OrderItem   oi ON o.OrderID       = oi.OrderID
                JOIN Item        i  ON oi.ItemID       = i.ItemID
                WHERE o.CustomerID = @custId
                GROUP BY o.OrderID, o.ORD_OrderDate, o.ORD_Status, o.ORD_TotalAmount,
                         l.LOC_StoreName, l.LOC_City, p.PAY_TypeName
                ORDER BY o.ORD_OrderDate DESC");
            cmd.Parameters.AddWithValue("@custId", custId);

            var dt = await FillDataTableViaCommandAsync(cmd, logger);

            var rows = new StringBuilder();
            if (dt == null || dt.Rows.Count == 0)
            {
                rows.Append(@"<tr>
                    <td colspan='6' class='text-center text-muted fst-italic py-4'>
                        No orders yet &mdash; time to start your first frabjous adventure!
                    </td>
                </tr>");
            }
            else
            {
                foreach (DataRow row in dt.Rows)
                {
                    int    oid      = Convert.ToInt32(row["OrderID"]);
                    string date     = row["ORD_OrderDate"] == DBNull.Value
                        ? "&mdash;" : Convert.ToDateTime(row["ORD_OrderDate"]).ToString("g");
                    string loc      = H(row["LOC_StoreName"]?.ToString() ?? "");
                    string items    = H(row["Items"]?.ToString() ?? "");
                    string total    = row["ORD_TotalAmount"] == DBNull.Value
                        ? "&mdash;" : $"${Convert.ToDecimal(row["ORD_TotalAmount"]):F2}";
                    string status   = H(row["ORD_Status"]?.ToString() ?? "");
                    string badge    = status.ToLower() switch
                    {
                        "pending"   => "bg-warning text-dark",
                        "completed" => "bg-success",
                        "cancelled" => "bg-danger",
                        _           => "bg-secondary"
                    };

                    rows.Append($@"<tr>
                        <td class='fw-semibold'>#{oid}</td>
                        <td class='text-nowrap'>{date}</td>
                        <td>{loc}</td>
                        <td><small class='text-muted'>{items}</small></td>
                        <td class='fw-bold text-nowrap'>{total}</td>
                        <td><span class='badge {badge}'>{status}</span></td>
                    </tr>");
                }
            }

            string body = $@"
<div class='container py-4'>
  <div class='d-flex justify-content-between align-items-center mb-4'>
    <h1 class='jj-brand fw-bold' style='color:#ea580c;'>My Order History</h1>
    <a href='/order' class='btn btn-warning fw-semibold'>New Order</a>
  </div>
  <div class='card shadow'>
    <div class='card-body p-0'>
      <div class='table-responsive'>
        <table class='table table-striped table-hover align-middle mb-0'>
          <thead class='table-dark'>
            <tr>
              <th>Order #</th>
              <th>Date</th>
              <th>Location</th>
              <th>Items</th>
              <th>Total</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>{rows}</tbody>
        </table>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Order History", username, body, null, pendingCount), "text/html");
        }

        // GET /account  — Account settings
        private static IResult HandleAccountGet(HttpContext ctx)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username = GetCurrentUsername(ctx)!;

            string err = ctx.Request.Query["err"].ToString();
            string alert = err switch
            {
                "exists"  => "<div class='alert alert-danger'>That username is already taken.</div>",
                "same"    => "<div class='alert alert-warning'>That is already your username.</div>",
                "missing" => "<div class='alert alert-danger'>New username cannot be empty.</div>",
                "fail"    => "<div class='alert alert-danger'>Could not update username. Please try again.</div>",
                _         => ""
            };

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-10 col-md-6 col-lg-5'>
      <div class='card shadow'>
        <div class='card-header p-0'>
          <ul class='nav nav-tabs card-header-tabs px-3 pt-2'>
            <li class='nav-item'>
              <span class='nav-link active fw-semibold' style='color:#ea580c;cursor:default;'>
                Change Username
              </span>
            </li>
            <li class='nav-item'>
              <a class='nav-link text-muted' href='/history'>Order History</a>
            </li>
          </ul>
        </div>
        <div class='card-body p-4'>
          {alert}
          <p class='text-muted mb-4'>Logged in as <strong>{H(username)}</strong></p>
          <form method='post' action='/account' novalidate>
            <div class='mb-3'>
              <label for='newUsername' class='form-label fw-semibold'>New Username</label>
              <input type='text' id='newUsername' name='newUsername' class='form-control'
                     required maxlength='30' autocomplete='username' autofocus>
            </div>
            <button type='submit' class='btn btn-warning w-100 fw-semibold'>Save Username</button>
          </form>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Account", username, body), "text/html");
        }

        // GET /account/success  — Username change confirmation
        private static IResult HandleAccountSuccess(HttpContext ctx)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username = GetCurrentUsername(ctx)!;

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-10 col-md-6 col-lg-5'>
      <div class='card shadow border-warning border-2 text-center'>
        <div class='card-body p-5'>
          <div class='display-1 mb-3'>&#127811;</div>
          <h2 class='fw-bold jj-brand mb-2' style='color:#ea580c;'>Username Updated!</h2>
          <p class='lead fw-semibold mb-1'>Welcome, {H(username)}.</p>
          <p class='text-muted fst-italic mt-3 mb-4'>
            &ldquo;The vorpal blade went snicker-snack &mdash; your old name tumbled into the Tulgey Wood,
            and a fresh-squeezed identity emerged, brillig and beaming, ready to sip.&rdquo;
          </p>
          <a href='/home' class='btn btn-warning btn-lg fw-bold'>Back to Home</a>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Username Updated", username, body), "text/html");
        }

        // POST /account  — Save username change
        private static async Task<IResult> HandleAccountPost(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            int    custId      = GetCurrentUserId(ctx);
            string currentName = GetCurrentUsername(ctx)!;

            var form       = await ctx.Request.ReadFormAsync();
            string newName = form["newUsername"].ToString().Trim();

            if (string.IsNullOrEmpty(newName))
                return Results.Redirect("/account?err=missing");
            if (newName == currentName)
                return Results.Redirect("/account?err=same");
            if (await UsernameExistsAsync(newName, logger))
                return Results.Redirect("/account?err=exists");

            var cmd = new SqlCommand(
                "UPDATE Customer SET CUS_Username = @name WHERE CustomerID = @id");
            cmd.Parameters.AddWithValue("@name", newName);
            cmd.Parameters.AddWithValue("@id",   custId);

            bool ok = await ExecSqlCommandAsync(cmd, logger);
            if (!ok) return Results.Redirect("/account?err=fail");

            ctx.Session.SetString("uname", newName);
            return Results.Redirect("/account/success");
        }

        // GET /pickup  — List pending orders or redirect to the single one
        private static async Task<IResult> HandlePickupIndex(HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username = GetCurrentUsername(ctx)!;
            int    custId   = GetCurrentUserId(ctx);

            var cmd = new SqlCommand(@"
                SELECT o.OrderID, o.ORD_OrderDate, o.ORD_TotalAmount,
                       l.LOC_StoreName, l.LOC_City, l.LOC_State,
                       STRING_AGG(i.ITM_ItemName, ', ') AS Items
                FROM [Order] o
                JOIN Location  l  ON o.LocationID = l.LocationID
                JOIN OrderItem oi ON o.OrderID     = oi.OrderID
                JOIN Item      i  ON oi.ItemID     = i.ItemID
                WHERE o.CustomerID = @cid AND o.ORD_Status = 'Pending'
                GROUP BY o.OrderID, o.ORD_OrderDate, o.ORD_TotalAmount,
                         l.LOC_StoreName, l.LOC_City, l.LOC_State
                ORDER BY o.ORD_OrderDate ASC");
            cmd.Parameters.AddWithValue("@cid", custId);
            var dt = await FillDataTableViaCommandAsync(cmd, logger);

            if (dt == null || dt.Rows.Count == 0) return Results.Redirect("/home");
            if (dt.Rows.Count == 1) return Results.Redirect($"/pickup/{dt.Rows[0]["OrderID"]}");

            int pendingCount = dt.Rows.Count;
            var cards = new StringBuilder();
            foreach (DataRow row in dt.Rows)
            {
                int     oid   = Convert.ToInt32(row["OrderID"]);
                string  date  = row["ORD_OrderDate"] == DBNull.Value
                    ? "—" : Convert.ToDateTime(row["ORD_OrderDate"]).ToString("g");
                string  loc   = $"{H(row["LOC_StoreName"]?.ToString())} &mdash; " +
                                $"{H(row["LOC_City"]?.ToString())}, {H(row["LOC_State"]?.ToString())}";
                string  items = H(row["Items"]?.ToString() ?? "");
                string  total = row["ORD_TotalAmount"] == DBNull.Value
                    ? "—" : $"${Convert.ToDecimal(row["ORD_TotalAmount"]):F2}";
                cards.Append($@"
<div class='col'>
  <div class='card shadow h-100 border-success border-2'>
    <div class='card-body'>
      <h5 class='fw-bold'>Order #{oid}</h5>
      <p class='text-muted small mb-1'>{date}</p>
      <p class='mb-1'><strong>Location:</strong> {loc}</p>
      <p class='mb-2 text-muted small'>{items}</p>
      <p class='fw-bold mb-0'>{total}</p>
    </div>
    <div class='card-footer'>
      <a href='/pickup/{oid}' class='btn btn-success w-100 fw-semibold'>Select This Order</a>
    </div>
  </div>
</div>");
            }

            string body = $@"
<div class='container py-5'>
  <div class='text-center mb-5'>
    <h1 class='jj-brand fw-bold' style='color:#ea580c;'>Confirm Pickup</h1>
    <p class='lead text-muted'>You have <strong>{pendingCount}</strong> pending orders. Which are you picking up?</p>
  </div>
  <div class='row row-cols-1 row-cols-md-2 row-cols-lg-3 g-4'>
    {cards}
  </div>
</div>";

            return Results.Content(Layout("Pickup", username, body, null, pendingCount), "text/html");
        }

        // GET /pickup/{orderId}  — Confirm or cancel a specific pending order
        private static async Task<IResult> HandlePickupOrder(int orderId, HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username = GetCurrentUsername(ctx)!;
            int    custId   = GetCurrentUserId(ctx);
            int    pending  = await GetPendingOrderCountAsync(custId, logger);

            var orderCmd = new SqlCommand(@"
                SELECT o.OrderID, o.ORD_OrderDate, o.ORD_Status, o.ORD_TotalAmount, o.ORD_Notes,
                       l.LOC_StoreName, l.LOC_City, l.LOC_State,
                       p.PAY_TypeName
                FROM [Order] o
                JOIN Location    l ON o.LocationID    = l.LocationID
                JOIN PaymentType p ON o.PaymentTypeID = p.PaymentTypeID
                WHERE o.OrderID = @oid AND o.CustomerID = @cid");
            orderCmd.Parameters.AddWithValue("@oid", orderId);
            orderCmd.Parameters.AddWithValue("@cid", custId);
            var orderDt = await FillDataTableViaCommandAsync(orderCmd, logger);

            if (orderDt == null || orderDt.Rows.Count == 0) return Results.Redirect("/pickup");

            var oRow   = orderDt.Rows[0];
            string status = oRow["ORD_Status"]?.ToString() ?? "";
            if (!status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                return Results.Redirect("/history");

            string storeName = $"{H(oRow["LOC_StoreName"]?.ToString())} &mdash; " +
                               $"{H(oRow["LOC_City"]?.ToString())}, {H(oRow["LOC_State"]?.ToString())}";
            string payType   = H(oRow["PAY_TypeName"]?.ToString());
            string orderDate = oRow["ORD_OrderDate"] == DBNull.Value
                ? "—" : Convert.ToDateTime(oRow["ORD_OrderDate"]).ToString("f");
            string total     = oRow["ORD_TotalAmount"] == DBNull.Value
                ? "—" : $"${Convert.ToDecimal(oRow["ORD_TotalAmount"]):F2}";
            string notes     = (oRow["ORD_Notes"] != DBNull.Value && !string.IsNullOrWhiteSpace(oRow["ORD_Notes"]?.ToString()))
                ? $"<p class='text-muted small mb-2'><strong>Notes:</strong> {H(oRow["ORD_Notes"]?.ToString())}</p>"
                : "";

            var itemsCmd = new SqlCommand(@"
                SELECT i.ITM_ItemName, oi.ORI_Quantity, oi.ORI_UnitPrice, oi.ORI_Subtotal
                FROM OrderItem oi
                JOIN Item i ON oi.ItemID = i.ItemID
                WHERE oi.OrderID = @oid ORDER BY i.ITM_ItemName");
            itemsCmd.Parameters.AddWithValue("@oid", orderId);
            var itemsDt = await FillDataTableViaCommandAsync(itemsCmd, logger);

            var itemRows = new StringBuilder();
            if (itemsDt != null)
                foreach (DataRow row in itemsDt.Rows)
                    itemRows.Append($"<tr>" +
                        $"<td>{H(row["ITM_ItemName"]?.ToString())}</td>" +
                        $"<td class='text-center'>{Convert.ToInt32(row["ORI_Quantity"])}</td>" +
                        $"<td class='text-end'>${Convert.ToDecimal(row["ORI_UnitPrice"]):F2}</td>" +
                        $"<td class='text-end fw-semibold'>${Convert.ToDecimal(row["ORI_Subtotal"]):F2}</td>" +
                        $"</tr>");

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-12 col-md-8 col-lg-7'>
      <div class='card shadow border-success border-2'>
        <div class='card-header text-white fw-bold fs-5 text-center' style='background-color:#16a34a;'>
          &#x1F7E2; Confirm Pickup &mdash; Order #{orderId}
        </div>
        <div class='card-body p-4'>
          <div class='row mb-3'>
            <div class='col-6'><strong>Date:</strong> {orderDate}</div>
            <div class='col-6 text-end'><strong>Payment:</strong> {payType}</div>
          </div>
          <p class='mb-2'><strong>Location:</strong> {storeName}</p>
          {notes}
          <hr>
          <table class='table table-bordered mb-3'>
            <thead class='table-warning'>
              <tr>
                <th>Item</th><th class='text-center'>Qty</th>
                <th class='text-end'>Price</th><th class='text-end'>Subtotal</th>
              </tr>
            </thead>
            <tbody>{itemRows}</tbody>
            <tfoot class='table-light fw-bold'>
              <tr>
                <td colspan='3' class='text-end'>Total:</td>
                <td class='text-end'>{total}</td>
              </tr>
            </tfoot>
          </table>
          <p class='text-muted fst-italic text-center small'>
            Ready to pick up your order? Confirm below, or cancel if plans have changed.
          </p>
        </div>
        <div class='card-footer d-flex gap-3 justify-content-center py-3'>
          <form method='post' action='/pickup/{orderId}/confirm'>
            <button type='submit' class='btn btn-success btn-lg fw-bold px-5'>
              &#x2714; Confirm Pickup
            </button>
          </form>
          <form method='post' action='/pickup/{orderId}/cancel'>
            <button type='submit' class='btn btn-outline-danger btn-lg px-5'>
              &#x2716; Cancel Order
            </button>
          </form>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Pickup", username, body, null, pending), "text/html");
        }

        // POST /pickup/{orderId}/confirm  — Mark order Completed, store quote, redirect to success
        private static async Task<IResult> HandlePickupConfirm(int orderId, HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            int custId = GetCurrentUserId(ctx);

            // Verify ownership and Pending status before updating
            var checkCmd = new SqlCommand(
                "SELECT ORD_Status FROM [Order] WHERE OrderID = @oid AND CustomerID = @cid");
            checkCmd.Parameters.AddWithValue("@oid", orderId);
            checkCmd.Parameters.AddWithValue("@cid", custId);
            var checkDt = await FillDataTableViaCommandAsync(checkCmd, logger);
            if (checkDt == null || checkDt.Rows.Count == 0) return Results.Redirect("/pickup");
            if (!checkDt.Rows[0]["ORD_Status"].ToString()!
                    .Equals("Pending", StringComparison.OrdinalIgnoreCase))
                return Results.Redirect("/history");

            var updateCmd = new SqlCommand(
                "UPDATE [Order] SET ORD_Status = 'Completed' WHERE OrderID = @oid AND CustomerID = @cid");
            updateCmd.Parameters.AddWithValue("@oid", orderId);
            updateCmd.Parameters.AddWithValue("@cid", custId);
            await ExecSqlCommandAsync(updateCmd, logger);

            // Fetch drink names from this order to pick a quote
            var drinksCmd = new SqlCommand(@"
                SELECT DISTINCT i.ITM_ItemName
                FROM OrderItem oi JOIN Item i ON oi.ItemID = i.ItemID
                WHERE oi.OrderID = @oid");
            drinksCmd.Parameters.AddWithValue("@oid", orderId);
            var drinksDt = await FillDataTableViaCommandAsync(drinksCmd, logger);

            var drinkNames = new List<string>();
            if (drinksDt != null)
                foreach (DataRow row in drinksDt.Rows)
                    drinkNames.Add(row["ITM_ItemName"]?.ToString() ?? "");

            ctx.Session.SetString("pickupQuote", DrinkQuotes.GetRandom(drinkNames));
            return Results.Redirect($"/pickup/{orderId}/success");
        }

        // POST /pickup/{orderId}/cancel  — Mark order Cancelled, refund points if JWP order, go to history
        private static async Task<IResult> HandlePickupCancel(int orderId, HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            int custId = GetCurrentUserId(ctx);

            var checkCmd = new SqlCommand(
                "SELECT ORD_Status FROM [Order] WHERE OrderID = @oid AND CustomerID = @cid");
            checkCmd.Parameters.AddWithValue("@oid", orderId);
            checkCmd.Parameters.AddWithValue("@cid", custId);
            var checkDt = await FillDataTableViaCommandAsync(checkCmd, logger);
            if (checkDt == null || checkDt.Rows.Count == 0) return Results.Redirect("/pickup");
            if (!checkDt.Rows[0]["ORD_Status"].ToString()!
                    .Equals("Pending", StringComparison.OrdinalIgnoreCase))
                return Results.Redirect("/history");

            await CancelOrderWithRefundAsync(orderId, custId, logger,
                $"REFUND on cancelled Order #{orderId}");
            return Results.Redirect("/history");
        }

        // Shared cancel+refund logic used by both the HTTP handler and the expiry background service.
        // Uses OUTPUT DELETED so only the caller that actually flips the row to Cancelled issues the refund,
        // preventing double-refunds if a user cancel and the background service race.
        private static async Task CancelOrderWithRefundAsync(
            int orderId, int custId, ILogger? logger = null, string? refundNotes = null)
        {
            var cancelCmd = new SqlCommand(@"
                UPDATE [Order]
                SET    ORD_Status = 'Cancelled'
                OUTPUT DELETED.OrderID, DELETED.PaymentTypeID
                WHERE  OrderID    = @oid
                  AND  CustomerID = @cid
                  AND  ORD_Status = 'Pending'");
            cancelCmd.Parameters.AddWithValue("@oid", orderId);
            cancelCmd.Parameters.AddWithValue("@cid", custId);
            var cancelledDt = await FillDataTableViaCommandAsync(cancelCmd, logger);

            // 0 rows means the order was already cancelled/completed — nothing to refund
            if (cancelledDt == null || cancelledDt.Rows.Count == 0) return;

            int orderPayTypeId = Convert.ToInt32(cancelledDt.Rows[0]["PaymentTypeID"]);

            int jwpPayId = await GetJabberWonkPaymentTypeIdAsync(logger);
            if (jwpPayId <= 0 || orderPayTypeId != jwpPayId) return;

            var txLookupCmd = new SqlCommand(
                "SELECT TOP 1 JWT_PointsDelta FROM JabberWonkTransaction " +
                "WHERE OrderID = @oid AND CustomerID = @cid AND JWT_TransactionType = 'REDEEM'");
            txLookupCmd.Parameters.AddWithValue("@oid", orderId);
            txLookupCmd.Parameters.AddWithValue("@cid", custId);
            var txDt = await FillDataTableViaCommandAsync(txLookupCmd, logger);
            if (txDt == null || txDt.Rows.Count == 0) return;

            int pointsToRefund = Math.Abs(Convert.ToInt32(txDt.Rows[0]["JWT_PointsDelta"]));
            int currentBal     = await GetPointsBalanceAsync(custId, logger);
            int newBalance     = currentBal + pointsToRefund;

            var balCmd = new SqlCommand(
                "UPDATE Customer SET CUS_PointsBalance = @bal WHERE CustomerID = @cid");
            balCmd.Parameters.AddWithValue("@bal", newBalance);
            balCmd.Parameters.AddWithValue("@cid", custId);
            await ExecSqlCommandAsync(balCmd, logger);

            var refundTxCmd = new SqlCommand(@"
                INSERT INTO JabberWonkTransaction
                    (JWT_PointsDelta, JWT_TransactionType, JWT_BalanceAfter, JWT_Notes, CustomerID, OrderID)
                VALUES (@delta, 'REFUND', @after, @notes, @cid, @oid)");
            refundTxCmd.Parameters.AddWithValue("@delta", pointsToRefund);
            refundTxCmd.Parameters.AddWithValue("@after", newBalance);
            refundTxCmd.Parameters.AddWithValue("@notes", refundNotes ?? $"REFUND on cancelled Order #{orderId}");
            refundTxCmd.Parameters.AddWithValue("@cid",   custId);
            refundTxCmd.Parameters.AddWithValue("@oid",   orderId);
            await ExecSqlCommandAsync(refundTxCmd, logger);
        }

        // GET /pickup/{orderId}/success  — Pickup confirmed success page with drink quote
        private static async Task<IResult> HandlePickupSuccess(int orderId, HttpContext ctx, ILogger<Program> logger)
        {
            if (!IsAuthenticated(ctx)) return Results.Redirect("/login");
            string username     = GetCurrentUsername(ctx)!;
            int    pendingCount = await GetPendingOrderCountAsync(GetCurrentUserId(ctx), logger);

            string quote = ctx.Session.GetString("pickupQuote") ?? DrinkQuotes.Fallback;
            ctx.Session.Remove("pickupQuote");

            string body = $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-10 col-md-7 col-lg-6'>
      <div class='card shadow border-success border-3 text-center'>
        <div class='card-body p-5'>
          <div class='display-1 mb-3'>&#127811;</div>
          <h2 class='fw-bold jj-brand mb-1' style='color:#16a34a;'>Order Picked Up!</h2>
          <p class='text-muted mb-4'>Order #{orderId} is now complete.</p>
          <hr>
          <p class='fst-italic fs-5 mt-4 mb-4' style='color:#ea580c;'>
            &ldquo;{H(quote)}&rdquo;
          </p>
          <hr>
          <div class='d-flex flex-wrap gap-2 justify-content-center mt-4'>
            <a href='/order'   class='btn btn-warning fw-semibold'>Order Again</a>
            <a href='/history' class='btn btn-outline-primary'>Order History</a>
            <a href='/home'    class='btn btn-outline-secondary'>Home</a>
          </div>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Pickup Complete", username, body, null, pendingCount), "text/html");
        }

        // GET /error
        private static IResult HandleError(HttpContext ctx)
        {
            string body = @"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-sm-10 col-md-6 text-center'>
      <div class='card shadow border-danger border-2'>
        <div class='card-body p-5'>
          <h1 class='display-1 fw-bold text-danger jj-brand'>!</h1>
          <h2 class='card-title'>Something went wrong</h2>
          <p class='card-text text-muted'>
            The Jabberwock struck! Beware the Jubjub bird, and shun
            the frumious Bandersnatch.
          </p>
          <a href='/' class='btn btn-warning btn-lg mt-3 fw-bold'>Return Home</a>
        </div>
      </div>
    </div>
  </div>
</div>";

            return Results.Content(Layout("Error", null, body), "text/html");
        }

        // ─── ADMIN HELPERS ───────────────────────────────────────────────────

        private static string RowString(DataRow row, string columnName) =>
            row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? row[columnName]?.ToString() ?? ""
                : "";

        private static int RowInt(DataRow row, string columnName) =>
            row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToInt32(row[columnName])
                : 0;

        private static decimal RowDecimal(DataRow row, string columnName) =>
            row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToDecimal(row[columnName])
                : 0m;

        private static DateTime? RowDate(DataRow row, string columnName) =>
            row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value
                ? Convert.ToDateTime(row[columnName])
                : null;

        private static async Task<DataTable?> GetAdminAccessibleLocationsAsync(int adminId, bool isGlobal, ILogger? logger = null)
        {
            var cmd = new SqlCommand(isGlobal
                ? @"
                    SELECT LocationID,
                           COALESCE(LOC_StoreName, 'Store #' + CAST(LocationID AS VARCHAR(10))) AS StoreName,
                           COALESCE(LOC_City, '') AS City,
                           COALESCE(LOC_State, '') AS State
                    FROM Location
                    ORDER BY COALESCE(LOC_StoreName, ''), LocationID"
                : @"
                    SELECT l.LocationID,
                           COALESCE(l.LOC_StoreName, 'Store #' + CAST(l.LocationID AS VARCHAR(10))) AS StoreName,
                           COALESCE(l.LOC_City, '') AS City,
                           COALESCE(l.LOC_State, '') AS State
                    FROM AdminLocationAccess ala
                    JOIN Location l ON l.LocationID = ala.LocationID
                    WHERE ala.AdminUserID = @adminId
                    ORDER BY COALESCE(l.LOC_StoreName, ''), l.LocationID");

            if (!isGlobal)
                cmd.Parameters.AddWithValue("@adminId", adminId);

            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static int? ResolveAdminLocationId(DataTable locationsDt, int? requestedLocationId, bool isGlobal)
        {
            if (locationsDt.Rows.Count == 0) return null;

            if (!requestedLocationId.HasValue)
                return isGlobal ? null : Convert.ToInt32(locationsDt.Rows[0]["LocationID"]);

            foreach (DataRow row in locationsDt.Rows)
                if (Convert.ToInt32(row["LocationID"]) == requestedLocationId.Value)
                    return requestedLocationId.Value;

            return isGlobal ? null : Convert.ToInt32(locationsDt.Rows[0]["LocationID"]);
        }

        private static async Task<int?> GetCustomerIdForAdminOrderAsync(int orderId, int? locationId, ILogger? logger = null)
        {
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";
            var cmd = new SqlCommand($@"
                SELECT TOP 1 o.CustomerID
                FROM [Order] o
                WHERE o.OrderID = @oid{locationFilter}");
            cmd.Parameters.AddWithValue("@oid", orderId);
            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);

            var dt = await FillDataTableViaCommandAsync(cmd, logger);
            if (dt == null || dt.Rows.Count == 0) return null;
            return Convert.ToInt32(dt.Rows[0]["CustomerID"]);
        }

        private static async Task<DataTable?> GetAdminOverviewMetricsAsync(string metricRange, int? locationId, ILogger? logger = null)
        {
            string orderFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";
            string caseFilter = locationId.HasValue ? " AND sc.LocationID = @loc" : "";
            string pointsFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";
            string completedDateFilter = metricRange switch
            {
                "week" => " AND o.ORD_OrderDate >= DATEADD(DAY, -7, GETDATE())",
                "month" => " AND o.ORD_OrderDate >= DATEADD(MONTH, -1, GETDATE())",
                _ => " AND CAST(o.ORD_OrderDate AS DATE) = CAST(GETDATE() AS DATE)"
            };
            string pointsDateFilter = metricRange switch
            {
                "week" => " AND j.JWT_TransactionDate >= DATEADD(DAY, -7, GETDATE())",
                "month" => " AND j.JWT_TransactionDate >= DATEADD(MONTH, -1, GETDATE())",
                _ => " AND CAST(j.JWT_TransactionDate AS DATE) = CAST(GETDATE() AS DATE)"
            };

            var cmd = new SqlCommand($@"
                SELECT
                    (SELECT ISNULL(SUM(o.ORD_TotalAmount), 0)
                     FROM [Order] o
                     WHERE o.ORD_Status = 'Completed'
                       {completedDateFilter}{orderFilter}) AS SalesTotal,
                    (SELECT COUNT(1)
                     FROM SupportCase sc
                     WHERE sc.SC_Status IN ('Open', 'In Review'){caseFilter}) AS OpenCases,
                    (SELECT COUNT(1)
                     FROM [Order] o
                     WHERE o.ORD_Status = 'Pending'{orderFilter}) AS PendingOrders,
                    (SELECT CAST(ISNULL(AVG(CAST(o.ORD_TotalAmount AS DECIMAL(10,2))), 0) AS DECIMAL(10,2))
                     FROM [Order] o
                     WHERE o.ORD_Status = 'Completed'
                       {completedDateFilter}{orderFilter}) AS AvgOrderValue,
                    (SELECT ISNULL(SUM(ABS(j.JWT_PointsDelta)), 0)
                     FROM JabberWonkTransaction j
                     JOIN [Order] o ON o.OrderID = j.OrderID
                     WHERE j.JWT_TransactionType = 'REDEEM'
                       {pointsDateFilter}{pointsFilter}) AS PointsRedeemedTotal");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> SearchAdminCustomersAsync(string q, int adminId, bool isGlobal, ILogger? logger = null)
        {
            // Non-global admins only see customers who have placed at least one order at an accessible location
            string accessFilter = isGlobal
                ? ""
                : @"AND EXISTS (
                      SELECT 1 FROM [Order] o2
                      JOIN AdminLocationAccess ala ON ala.LocationID = o2.LocationID
                      WHERE o2.CustomerID = c.CustomerID AND ala.AdminUserID = @adminId)";

            var cmd = new SqlCommand($@"
                SELECT TOP 8
                    c.CustomerID, c.CUS_FirstName, c.CUS_LastName, c.CUS_Username,
                    c.CUS_Email, c.CUS_Phone, c.CUS_PointsBalance
                FROM Customer c
                WHERE (@q = ''
                   OR c.CUS_FirstName LIKE @like
                   OR c.CUS_LastName LIKE @like
                   OR c.CUS_Username LIKE @like
                   OR ISNULL(c.CUS_Email, '') LIKE @like
                   OR ISNULL(c.CUS_Phone, '') LIKE @like)
                {accessFilter}
                ORDER BY CASE WHEN c.CUS_Username = @q THEN 0 ELSE 1 END,
                         c.CustomerID DESC");
            cmd.Parameters.AddWithValue("@q", q);
            cmd.Parameters.AddWithValue("@like", $"%{q}%");
            if (!isGlobal) cmd.Parameters.AddWithValue("@adminId", adminId);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminCustomerDetailAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand(@"
                SELECT TOP 1
                    CustomerID, CUS_FirstName, CUS_LastName, CUS_Username, CUS_Email,
                    CUS_Phone, CUS_Address, CUS_City, CUS_State, CUS_ZipCode, CUS_PointsBalance
                FROM Customer
                WHERE CustomerID = @cid");
            cmd.Parameters.AddWithValue("@cid", customerId);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminCustomerOrdersAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand(@"
                SELECT TOP 6
                    o.OrderID, o.ORD_OrderDate, o.ORD_Status, o.ORD_TotalAmount,
                    COALESCE(l.LOC_StoreName, 'Unknown') AS StoreName,
                    COALESCE(p.PAY_TypeName, 'Unknown') AS PaymentType
                FROM [Order] o
                LEFT JOIN Location l ON l.LocationID = o.LocationID
                LEFT JOIN PaymentType p ON p.PaymentTypeID = o.PaymentTypeID
                WHERE o.CustomerID = @cid
                ORDER BY o.ORD_OrderDate DESC");
            cmd.Parameters.AddWithValue("@cid", customerId);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminCustomerPointsAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand(@"
                SELECT TOP 8
                    JWT_TransactionDate, JWT_TransactionType, JWT_PointsDelta,
                    JWT_BalanceAfter, OrderID, JWT_Notes
                FROM JabberWonkTransaction
                WHERE CustomerID = @cid
                ORDER BY JWT_TransactionDate DESC");
            cmd.Parameters.AddWithValue("@cid", customerId);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminCustomerCasesAsync(int customerId, ILogger? logger = null)
        {
            var cmd = new SqlCommand(@"
                SELECT TOP 6
                    sc.SupportCaseID, sc.SC_Status, sc.SC_Priority, sc.SC_Category,
                    sc.SC_Subject, sc.SC_CreatedAt,
                    COALESCE(a.ADM_DisplayName, a.ADM_Username, 'Unassigned') AS AssignedAdmin
                FROM SupportCase sc
                LEFT JOIN AdminUser a ON a.AdminUserID = sc.AssignedToAdminUserID
                WHERE sc.CustomerID = @cid
                ORDER BY sc.SC_CreatedAt DESC");
            cmd.Parameters.AddWithValue("@cid", customerId);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminLiveOrdersAsync(int? locationId, ILogger? logger = null)
        {
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";
            var cmd = new SqlCommand($@"
                SELECT TOP 12
                    o.OrderID,
                    o.CustomerID,
                    o.ORD_OrderDate,
                    o.ORD_Status,
                    o.ORD_TotalAmount,
                    DATEDIFF(MINUTE, o.ORD_OrderDate, GETDATE()) AS AgeMinutes,
                    COALESCE(c.CUS_Username, 'guest') AS Username,
                    COALESCE(l.LOC_StoreName, 'Unknown') AS StoreName,
                    COALESCE(p.PAY_TypeName, 'Unknown') AS PaymentType
                FROM [Order] o
                JOIN Customer c ON c.CustomerID = o.CustomerID
                LEFT JOIN Location l ON l.LocationID = o.LocationID
                LEFT JOIN PaymentType p ON p.PaymentTypeID = o.PaymentTypeID
                WHERE o.ORD_Status IN ('Pending', 'Completed', 'Cancelled')
                  AND (o.ORD_Status = 'Pending' OR o.ORD_OrderDate >= DATEADD(DAY, -1, GETDATE())){locationFilter}
                ORDER BY CASE WHEN o.ORD_Status = 'Pending' THEN 0 ELSE 1 END,
                         o.ORD_OrderDate DESC");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminSalesTrendAsync(string grain, int? locationId, ILogger? logger = null)
        {
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";
            string sql = grain switch
            {
                "hour" => $@"
                    SELECT
                        CONVERT(VARCHAR(5), DATEADD(HOUR, DATEDIFF(HOUR, 0, o.ORD_OrderDate), 0), 108) AS BucketLabel,
                        DATEADD(HOUR, DATEDIFF(HOUR, 0, o.ORD_OrderDate), 0) AS BucketSort,
                        COUNT(1) AS OrderCount,
                        ISNULL(SUM(o.ORD_TotalAmount), 0) AS Sales
                    FROM [Order] o
                    WHERE o.ORD_Status = 'Completed'
                      AND CAST(o.ORD_OrderDate AS DATE) = CAST(GETDATE() AS DATE){locationFilter}
                    GROUP BY DATEADD(HOUR, DATEDIFF(HOUR, 0, o.ORD_OrderDate), 0)
                    ORDER BY BucketSort",
                "week" => $@"
                    SELECT
                        CONCAT('Week of ', CONVERT(VARCHAR(10), DATEADD(WEEK, DATEDIFF(WEEK, 0, o.ORD_OrderDate), 0), 120)) AS BucketLabel,
                        DATEADD(WEEK, DATEDIFF(WEEK, 0, o.ORD_OrderDate), 0) AS BucketSort,
                        COUNT(1) AS OrderCount,
                        ISNULL(SUM(o.ORD_TotalAmount), 0) AS Sales
                    FROM [Order] o
                    WHERE o.ORD_Status = 'Completed'
                      AND o.ORD_OrderDate >= DATEADD(WEEK, -7, GETDATE()){locationFilter}
                    GROUP BY DATEADD(WEEK, DATEDIFF(WEEK, 0, o.ORD_OrderDate), 0)
                    ORDER BY BucketSort",
                _ => $@"
                    SELECT
                        CONVERT(VARCHAR(10), CAST(o.ORD_OrderDate AS DATE), 120) AS BucketLabel,
                        CAST(o.ORD_OrderDate AS DATE) AS BucketSort,
                        COUNT(1) AS OrderCount,
                        ISNULL(SUM(o.ORD_TotalAmount), 0) AS Sales
                    FROM [Order] o
                    WHERE o.ORD_Status = 'Completed'
                      AND o.ORD_OrderDate >= DATEADD(DAY, -6, CAST(GETDATE() AS DATE)){locationFilter}
                    GROUP BY CAST(o.ORD_OrderDate AS DATE)
                    ORDER BY BucketSort"
            };

            var cmd = new SqlCommand(sql);
            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminRecentActivityAsync(int? locationId, ILogger? logger = null)
        {
            string orderFilter = locationId.HasValue ? " WHERE o.LocationID = @loc" : "";
            string pointsFilter = locationId.HasValue ? " WHERE o.LocationID = @loc" : "";
            string caseFilter = locationId.HasValue ? " WHERE sc.LocationID = @loc" : "";
            string auditFilter = locationId.HasValue ? " WHERE a.LocationID = @loc" : "";

            var cmd = new SqlCommand($@"
                SELECT TOP 50 ActivityAt, ActivityType, Title, Detail
                FROM (
                    SELECT
                        o.ORD_OrderDate AS ActivityAt,
                        'Order' AS ActivityType,
                        CONCAT('Order #', o.OrderID, ' ', o.ORD_Status) AS Title,
                        CONCAT(COALESCE(c.CUS_Username, 'guest'), ' at ', COALESCE(l.LOC_StoreName, 'Unknown')) AS Detail
                    FROM [Order] o
                    JOIN Customer c ON c.CustomerID = o.CustomerID
                    LEFT JOIN Location l ON l.LocationID = o.LocationID
                    {orderFilter}

                    UNION ALL

                    SELECT
                        j.JWT_TransactionDate AS ActivityAt,
                        'Points' AS ActivityType,
                        CONCAT(j.JWT_TransactionType, ' on Order #', j.OrderID) AS Title,
                        CONCAT(COALESCE(c.CUS_Username, 'guest'), ' delta ', j.JWT_PointsDelta) AS Detail
                    FROM JabberWonkTransaction j
                    JOIN Customer c ON c.CustomerID = j.CustomerID
                    JOIN [Order] o ON o.OrderID = j.OrderID
                    {pointsFilter}

                    UNION ALL

                    SELECT
                        sc.SC_CreatedAt AS ActivityAt,
                        'Case' AS ActivityType,
                        CONCAT(sc.SC_Category, ': ', sc.SC_Subject) AS Title,
                        CONCAT('Status ', sc.SC_Status, ' / Priority ', sc.SC_Priority) AS Detail
                    FROM SupportCase sc
                    {caseFilter}

                    UNION ALL

                    SELECT
                        a.AAL_CreatedAt AS ActivityAt,
                        'Admin' AS ActivityType,
                        CONCAT(a.AAL_ActionType, ' ', a.AAL_EntityType) AS Title,
                        ISNULL(a.AAL_Details, 'No details recorded') AS Detail
                    FROM AdminAuditLog a
                    {auditFilter}
                ) src
                ORDER BY ActivityAt DESC");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminPopularItemsAsync(string grain, int? locationId, ILogger? logger = null)
        {
            string dateFilter = grain == "week"
                ? " AND o.ORD_OrderDate >= DATEADD(DAY, -7, GETDATE())"
                : " AND CAST(o.ORD_OrderDate AS DATE) = CAST(GETDATE() AS DATE)";
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";

            var cmd = new SqlCommand($@"
                SELECT TOP 6
                    i.ItemID,
                    i.ITM_ItemName,
                    COALESCE(i.ITM_Category, 'Uncategorized') AS ITM_Category,
                    SUM(oi.ORI_Quantity) AS UnitsSold,
                    SUM(oi.ORI_Quantity * oi.ORI_UnitPrice) AS Revenue
                FROM OrderItem oi
                JOIN [Order] o ON o.OrderID = oi.OrderID
                JOIN Item i ON i.ItemID = oi.ItemID
                WHERE o.ORD_Status = 'Completed'{dateFilter}{locationFilter}
                GROUP BY i.ItemID, i.ITM_ItemName, i.ITM_Category
                ORDER BY UnitsSold DESC, Revenue DESC");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminPaymentMixAsync(string grain, int? locationId, ILogger? logger = null)
        {
            string dateFilter = grain == "week"
                ? " AND o.ORD_OrderDate >= DATEADD(DAY, -7, GETDATE())"
                : grain == "hour"
                    ? " AND CAST(o.ORD_OrderDate AS DATE) = CAST(GETDATE() AS DATE)"
                    : " AND o.ORD_OrderDate >= DATEADD(DAY, -6, CAST(GETDATE() AS DATE))";
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";

            var cmd = new SqlCommand($@"
                SELECT
                    COALESCE(p.PAY_TypeName, 'Unknown') AS PaymentType,
                    COUNT(1) AS OrderCount,
                    ISNULL(SUM(o.ORD_TotalAmount), 0) AS Sales
                FROM [Order] o
                LEFT JOIN PaymentType p ON p.PaymentTypeID = o.PaymentTypeID
                WHERE o.ORD_Status = 'Completed'{dateFilter}{locationFilter}
                GROUP BY COALESCE(p.PAY_TypeName, 'Unknown')
                ORDER BY OrderCount DESC, Sales DESC");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminPointsMixAsync(string grain, int? locationId, ILogger? logger = null)
        {
            string dateFilter = grain == "week"
                ? " AND j.JWT_TransactionDate >= DATEADD(DAY, -7, GETDATE())"
                : grain == "hour"
                    ? " AND CAST(j.JWT_TransactionDate AS DATE) = CAST(GETDATE() AS DATE)"
                    : " AND j.JWT_TransactionDate >= DATEADD(DAY, -6, CAST(GETDATE() AS DATE))";
            string locationFilter = locationId.HasValue ? " AND o.LocationID = @loc" : "";

            var cmd = new SqlCommand($@"
                SELECT
                    ISNULL(SUM(CASE WHEN j.JWT_TransactionType = 'EARN' THEN j.JWT_PointsDelta ELSE 0 END), 0) AS PointsEarned,
                    ISNULL(SUM(CASE WHEN j.JWT_TransactionType = 'REDEEM' THEN ABS(j.JWT_PointsDelta) ELSE 0 END), 0) AS PointsRedeemed,
                    ISNULL(SUM(CASE WHEN j.JWT_TransactionType = 'REFUND' THEN j.JWT_PointsDelta ELSE 0 END), 0) AS PointsRefunded
                FROM JabberWonkTransaction j
                JOIN [Order] o ON o.OrderID = j.OrderID
                WHERE 1 = 1{dateFilter}{locationFilter}");

            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static async Task<DataTable?> GetAdminLocationPerformanceAsync(bool isGlobal, int adminId, int? locationId, ILogger? logger = null)
        {
            string accessJoin = isGlobal
                ? ""
                : "JOIN AdminLocationAccess ala ON ala.LocationID = l.LocationID AND ala.AdminUserID = @adminId";
            string locationFilter = locationId.HasValue ? " AND l.LocationID = @loc" : "";

            var cmd = new SqlCommand($@"
                SELECT
                    l.LocationID,
                    COALESCE(l.LOC_StoreName, 'Store #' + CAST(l.LocationID AS VARCHAR(10))) AS StoreName,
                    COUNT(CASE WHEN o.ORD_Status = 'Completed' THEN 1 END) AS CompletedOrders,
                    COUNT(CASE WHEN o.ORD_Status = 'Pending' THEN 1 END) AS PendingOrders,
                    COUNT(CASE WHEN o.ORD_Status = 'Cancelled' THEN 1 END) AS CancelledOrders,
                    CAST(ISNULL(SUM(CASE WHEN o.ORD_Status = 'Completed' THEN o.ORD_TotalAmount ELSE 0 END), 0) AS DECIMAL(10,2)) AS Revenue,
                    CAST(ISNULL(AVG(CASE WHEN o.ORD_Status = 'Completed' THEN o.ORD_TotalAmount END), 0) AS DECIMAL(10,2)) AS AvgTicket
                FROM Location l
                {accessJoin}
                LEFT JOIN [Order] o
                    ON o.LocationID = l.LocationID
                   AND o.ORD_OrderDate >= DATEADD(DAY, -7, GETDATE())
                WHERE 1 = 1{locationFilter}
                GROUP BY l.LocationID, l.LOC_StoreName
                ORDER BY Revenue DESC, CompletedOrders DESC");

            if (!isGlobal) cmd.Parameters.AddWithValue("@adminId", adminId);
            if (locationId.HasValue) cmd.Parameters.AddWithValue("@loc", locationId.Value);
            return await FillDataTableViaCommandAsync(cmd, logger);
        }

        private static string RenderAdminSetupBody(string adminDisplay, string headline, string detail)
        {
            return $@"
<div class='container py-5'>
  <div class='row justify-content-center'>
    <div class='col-lg-8'>
      <div class='card shadow border-warning border-2'>
        <div class='card-body p-5'>
          <span class='badge rounded-pill border border-danger bg-white px-3 py-2 fw-semibold mb-3'
                style='color:#b91c1c;'>Demo Admin</span>
          <h1 class='jj-brand fw-bold mb-3' style='color:#ea580c;'>Admin Dashboard</h1>
          <p class='text-muted'>Signed in as {H(adminDisplay)}</p>
          <div class='alert alert-warning mb-4'>
            <strong>{H(headline)}</strong><br>{H(detail)}
          </div>
          <div class='d-flex gap-2 flex-wrap'>
            <a class='btn btn-danger fw-semibold' href='/admin/login'>Back to Admin Login</a>
            <a class='btn btn-outline-secondary' href='/'>Return to Site</a>
          </div>
        </div>
      </div>
    </div>
  </div>
</div>";
        }

        private static string RenderAdminDashboardBody(
            string adminDisplay,
            string adminRole,
            bool isGlobal,
            string grain,
            string metricRange,
            string q,
            int? selectedLocationId,
            int? selectedCustomerId,
            int? selectedOrderId,
            string adjAlert,
            DataTable locationsDt,
            DataTable? overviewDt,
            DataTable? searchDt,
            DataTable? selectedCustomerDt,
            DataTable? selectedCustomerOrdersDt,
            DataTable? selectedCustomerPointsDt,
            DataTable? selectedCustomerCasesDt,
            DataTable? liveOrdersDt,
            DataTable? salesTrendDt,
            DataTable? activityDt,
            DataTable? popularItemsDt,
            DataTable? paymentMixDt,
            DataTable? pointsMixDt,
            DataTable? locationPerfDt)
        {
            string locationOptions = isGlobal
                ? $"<option value=''{(!selectedLocationId.HasValue ? " selected" : "")}>All stores</option>"
                : "";

            foreach (DataRow row in locationsDt.Rows)
            {
                int id = Convert.ToInt32(row["LocationID"]);
                string selected = selectedLocationId == id ? " selected" : "";
                string label = H($"{RowString(row, "StoreName")} {RowString(row, "City")} {RowString(row, "State")}".Trim());
                locationOptions += $"<option value='{id}'{selected}>{label}</option>";
            }

            string body = $@"
<style>
  .admin-shell .admin-card {{ border: 1px solid #e5e7eb; border-radius: 1rem; box-shadow: 0 0.5rem 1rem rgba(0,0,0,.06); }}
  .admin-shell .metric-card {{ background: linear-gradient(180deg, #fff 0%, #fff7ed 100%); }}
  .admin-shell .section-title {{ font-size: 1.05rem; font-weight: 700; color: #7c2d12; }}
  .admin-shell .mini-label {{ font-size: .78rem; text-transform: uppercase; letter-spacing: .06em; color: #6b7280; }}
  .admin-shell .search-link {{ text-decoration: none; }}
  .admin-shell .trend-bar {{ height: 10px; background: #fdba74; border-radius: 999px; }}
  .admin-shell .subtle-box {{ background: #f8fafc; border: 1px dashed #cbd5e1; border-radius: .85rem; }}
  .admin-shell .activity-scroll {{ max-height: 30rem; overflow-y: auto; padding-right: .25rem; }}
  .admin-shell .activity-entry {{ display: flex; align-items: center; gap: .75rem; white-space: nowrap; overflow: hidden; }}
  .admin-shell .activity-entry > * {{ min-width: 0; }}
  .admin-shell .activity-time {{ flex: 0 0 9rem; color: #6b7280; font-size: .85rem; }}
  .admin-shell .activity-type {{ flex: 0 0 auto; }}
  .admin-shell .activity-title {{ flex: 0 1 18rem; font-weight: 600; overflow: hidden; text-overflow: ellipsis; }}
  .admin-shell .activity-detail {{ flex: 1 1 auto; color: #6b7280; overflow: hidden; text-overflow: ellipsis; }}
  @media (max-width: 991.98px) {{
    .admin-shell .activity-entry {{ display: block; white-space: normal; }}
    .admin-shell .activity-time {{ display: block; margin-bottom: .35rem; }}
    .admin-shell .activity-type {{ display: inline-flex; margin-right: .5rem; margin-bottom: .35rem; }}
    .admin-shell .activity-title,
    .admin-shell .activity-detail {{ display: block; overflow: visible; text-overflow: clip; }}
  }}
</style>
<div class='container-fluid py-4 admin-shell'>
  <div class='d-flex flex-wrap justify-content-between align-items-start gap-3 mb-4'>
    <div>
      <div class='d-flex align-items-center gap-2 mb-2'>
        <span class='badge rounded-pill border border-danger bg-white px-3 py-2 fw-semibold'
              style='color:#b91c1c;'>Demo Admin</span>
        <span class='badge text-bg-dark'>{H(adminRole)}</span>
        <span class='badge text-bg-secondary'>{(isGlobal ? "Global Access" : "Local Access")}</span>
      </div>
      <h1 class='jj-brand fw-bold mb-1' style='color:#ea580c;'>Admin Dashboard</h1>
      <p class='text-muted mb-0'>Signed in as {H(adminDisplay)}</p>
    </div>
    <div class='d-flex gap-2 flex-wrap'>
      <a class='btn btn-outline-secondary' href='/home'>Customer Home</a>
      <a class='btn btn-danger' href='/admin/logout'>Admin Logout</a>
    </div>
  </div>

  <div class='row g-3 mb-4'>
    <div class='col-xl-8'>
      <div class='card admin-card'>
        <div class='card-body'>
          <div class='section-title mb-3'>Dashboard Filters</div>
          <form method='get' action='/admin' class='row g-3 align-items-end'>
            <div class='col-md-4'>
              <label class='form-label fw-semibold'>Store</label>
                      <select class='form-select' name='locationId'>{locationOptions}</select>
            </div>
            <div class='col-md-3'>
              <label class='form-label fw-semibold'>Trend View</label>
              <select class='form-select' name='grain'>
                <option value='day'{(grain == "day" ? " selected" : "")}>Day</option>
                <option value='hour'{(grain == "hour" ? " selected" : "")}>Hour</option>
                <option value='week'{(grain == "week" ? " selected" : "")}>Week</option>
              </select>
            </div>
            <div class='col-md-5'>
              <label class='form-label fw-semibold'>Customer or Order Search</label>
              <input class='form-control' type='text' name='q' value='{H(q)}'
                     placeholder='Search username, name, email, phone'>
            </div>
            <input type='hidden' name='metricRange' value='{H(metricRange)}'>
            <div class='col-12 d-flex gap-2 flex-wrap'>
              <button class='btn btn-warning fw-semibold' type='submit'>Apply Filters</button>
              <a class='btn btn-outline-secondary' href='/admin'>Reset</a>
            </div>
          </form>
        </div>
      </div>
    </div>
    <div class='col-xl-4'>
      {RenderAdminMetricCards(overviewDt, metricRange, grain, selectedLocationId, q, selectedCustomerId, selectedOrderId)}
    </div>
  </div>

  <div class='row g-4'>
    <div class='col-xxl-4 col-xl-5'>
      {RenderAdminCustomerWorkspace(q, grain, metricRange, selectedLocationId, selectedCustomerId, selectedOrderId, searchDt, selectedCustomerDt, selectedCustomerOrdersDt, selectedCustomerCasesDt)}
      <div class='mt-4'>
        {RenderAdminPointsManager(selectedCustomerDt, selectedCustomerPointsDt, grain, metricRange, selectedLocationId, q, adjAlert)}
      </div>
    </div>
    <div class='col-xxl-5 col-xl-7'>
      {RenderAdminLiveOrders(grain, metricRange, selectedLocationId, liveOrdersDt)}
      <div class='mt-4'>
        {RenderAdminSalesTrend(grain, salesTrendDt)}
      </div>
    </div>
    <div class='col-xxl-3 col-xl-12'>
      {RenderAdminPopularItems(popularItemsDt)}
      <div class='mt-4'>
        {RenderAdminPaymentMix(paymentMixDt, pointsMixDt)}
      </div>
    </div>
  </div>

  <div class='row g-4 mt-1'>
    <div class='col-12'>
      {RenderAdminLocationPerformance(locationPerfDt, isGlobal)}
    </div>
  </div>
  <div class='row g-4 mt-1'>
    <div class='col-12'>
      {RenderAdminActivityFeed(activityDt)}
    </div>
  </div>
</div>";

            return body;
        }

        private static string RenderAdminMetricCards(DataTable? overviewDt, string metricRange, string grain,
            int? locationId, string q, int? selectedCustomerId, int? selectedOrderId)
        {
            decimal sales = 0m;
            int openCases = 0;
            int pendingOrders = 0;
            decimal avgOrder = 0m;
            int redeemedPoints = 0;
            string rangeLabel = metricRange switch
            {
                "week" => "This Week",
                "month" => "This Month",
                _ => "Today"
            };

            if (overviewDt != null && overviewDt.Rows.Count > 0)
            {
                var row = overviewDt.Rows[0];
                sales = RowDecimal(row, "SalesTotal");
                openCases = RowInt(row, "OpenCases");
                pendingOrders = RowInt(row, "PendingOrders");
                avgOrder = RowDecimal(row, "AvgOrderValue");
                redeemedPoints = RowInt(row, "PointsRedeemedTotal");
            }

            string dayUrl = BuildAdminUrl(grain, locationId, q, selectedCustomerId, selectedOrderId, "day");
            string weekUrl = BuildAdminUrl(grain, locationId, q, selectedCustomerId, selectedOrderId, "week");
            string monthUrl = BuildAdminUrl(grain, locationId, q, selectedCustomerId, selectedOrderId, "month");

            return $@"
<div class='card admin-card metric-card h-100'>
  <div class='card-body'>
    <div class='d-flex justify-content-between align-items-center gap-2 mb-3'>
      <div class='section-title mb-0'>At-a-Glance Metrics</div>
      <div class='btn-group btn-group-sm' role='group' aria-label='Metrics range'>
        <a class='btn {(metricRange == "day" ? "btn-danger" : "btn-outline-danger")}' href='{dayUrl}'>D</a>
        <a class='btn {(metricRange == "week" ? "btn-danger" : "btn-outline-danger")}' href='{weekUrl}'>W</a>
        <a class='btn {(metricRange == "month" ? "btn-danger" : "btn-outline-danger")}' href='{monthUrl}'>M</a>
      </div>
    </div>
    <div class='small text-muted mb-3'>Current overview range: {H(rangeLabel)}</div>
    <div class='row g-3'>
      <div class='col-sm-6'>
        <div class='subtle-box p-3 h-100'>
          <div class='mini-label'>{H(rangeLabel)} Sales</div>
          <div class='fs-4 fw-bold text-success'>${sales:F2}</div>
        </div>
      </div>
      <div class='col-sm-6'>
        <div class='subtle-box p-3 h-100'>
          <div class='mini-label'>Open Cases</div>
          <div class='fs-4 fw-bold'>{openCases}</div>
        </div>
      </div>
      <div class='col-sm-6'>
        <div class='subtle-box p-3 h-100'>
          <div class='mini-label'>Pending Orders</div>
          <div class='fs-4 fw-bold'>{pendingOrders}</div>
        </div>
      </div>
      <div class='col-sm-6'>
        <div class='subtle-box p-3 h-100'>
          <div class='mini-label'>Avg Order Value</div>
          <div class='fs-4 fw-bold'>${avgOrder:F2}</div>
        </div>
      </div>
      <div class='col-12'>
        <div class='subtle-box p-3'>
          <div class='mini-label'>Points Redeemed {H(rangeLabel)}</div>
          <div class='fs-4 fw-bold text-warning-emphasis'>{redeemedPoints:N0} pts</div>
        </div>
      </div>
    </div>
  </div>
</div>";
        }

        private static string RenderAdminCustomerWorkspace(string q, string grain, string metricRange, int? locationId,
            int? selectedCustomerId, int? selectedOrderId, DataTable? searchDt, DataTable? customerDt,
            DataTable? ordersDt, DataTable? casesDt)
        {
            var results = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(q))
            {
                if (searchDt == null || searchDt.Rows.Count == 0)
                {
                    results.Append("<div class='alert alert-light border'>No customer matches found for this search.</div>");
                }
                else
                {
                    results.Append("<div class='list-group mb-3'>");
                    foreach (DataRow row in searchDt.Rows)
                    {
                        int cid = RowInt(row, "CustomerID");
                        string link = BuildAdminUrl(grain, locationId, q, cid, null, metricRange);
                        string name = H($"{RowString(row, "CUS_FirstName")} {RowString(row, "CUS_LastName")}".Trim());
                        string username = H(RowString(row, "CUS_Username"));
                        string email = H(RowString(row, "CUS_Email"));
                        string active = selectedCustomerId == cid ? " active" : "";
                        results.Append($@"
<a class='list-group-item list-group-item-action{active} search-link' href='{link}'>
  <div class='d-flex justify-content-between align-items-center'>
    <div>
      <div class='fw-semibold'>{name} <span class='text-muted'>@{username}</span></div>
      <div class='small text-muted'>{email}</div>
    </div>
    <span class='badge text-bg-warning'>{RowInt(row, "CUS_PointsBalance"):N0} pts</span>
  </div>
</a>");
                    }
                    results.Append("</div>");
                }
            }

            string detailHtml = "<div class='alert alert-light border mb-0'>Search for a customer to view profile, orders, and support cases.</div>";

            if (customerDt != null && customerDt.Rows.Count > 0)
            {
                DataRow customer = customerDt.Rows[0];
                string name = H($"{RowString(customer, "CUS_FirstName")} {RowString(customer, "CUS_LastName")}".Trim());
                string username = H(RowString(customer, "CUS_Username"));
                string email = H(RowString(customer, "CUS_Email"));
                string phone = H(RowString(customer, "CUS_Phone"));
                string address = H($"{RowString(customer, "CUS_Address")} {RowString(customer, "CUS_City")} {RowString(customer, "CUS_State")} {RowString(customer, "CUS_ZipCode")}".Trim());

                var ordersHtml = new StringBuilder();
                if (ordersDt == null || ordersDt.Rows.Count == 0)
                {
                    ordersHtml.Append("<div class='small text-muted'>No orders found.</div>");
                }
                else
                {
                    ordersHtml.Append("<div class='table-responsive'><table class='table table-sm align-middle mb-0'><thead><tr><th>Order</th><th>Status</th><th>Total</th></tr></thead><tbody>");
                    foreach (DataRow row in ordersDt.Rows)
                    {
                        int orderId = RowInt(row, "OrderID");
                        string link = BuildAdminUrl(grain, locationId, q, selectedCustomerId, orderId, metricRange);
                        ordersHtml.Append($@"
<tr>
  <td><a href='{link}'>#{orderId}</a><div class='small text-muted'>{H(RowString(row, "StoreName"))}</div></td>
  <td>{H(RowString(row, "ORD_Status"))}</td>
  <td>${RowDecimal(row, "ORD_TotalAmount"):F2}</td>
</tr>");
                    }
                    ordersHtml.Append("</tbody></table></div>");
                }

                var casesHtml = new StringBuilder();
                if (casesDt == null || casesDt.Rows.Count == 0)
                {
                    casesHtml.Append("<div class='small text-muted'>No support cases recorded.</div>");
                }
                else
                {
                    casesHtml.Append("<div class='table-responsive'><table class='table table-sm align-middle mb-0'><thead><tr><th>Case</th><th>Status</th><th>Priority</th></tr></thead><tbody>");
                    foreach (DataRow row in casesDt.Rows)
                    {
                        casesHtml.Append($@"
<tr>
  <td>#{RowInt(row, "SupportCaseID")}<div class='small text-muted'>{H(RowString(row, "SC_Subject"))}</div></td>
  <td>{H(RowString(row, "SC_Status"))}</td>
  <td>{H(RowString(row, "SC_Priority"))}</td>
</tr>");
                    }
                    casesHtml.Append("</tbody></table></div>");
                }

                detailHtml = $@"
<div class='subtle-box p-3 mb-3'>
  <div class='d-flex justify-content-between align-items-start gap-2'>
    <div>
      <div class='fw-bold fs-5'>{name}</div>
      <div class='text-muted'>@{username}</div>
    </div>
    <span class='badge text-bg-warning'>{RowInt(customer, "CUS_PointsBalance"):N0} pts</span>
  </div>
  <div class='mt-3 small'>
    <div><strong>Email:</strong> {email}</div>
    <div><strong>Phone:</strong> {phone}</div>
    <div><strong>Address:</strong> {address}</div>
    {(selectedOrderId.HasValue ? $"<div><strong>Focused Order:</strong> #{selectedOrderId.Value}</div>" : "")}
  </div>
</div>
<div class='row g-3'>
  <div class='col-lg-6'>
    <div class='subtle-box p-3 h-100'>
      <div class='mini-label mb-2'>Recent Orders</div>
      {ordersHtml}
    </div>
  </div>
  <div class='col-lg-6'>
    <div class='subtle-box p-3 h-100'>
      <div class='mini-label mb-2'>Support Cases</div>
      {casesHtml}
    </div>
  </div>
</div>";
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>1. Customer Lookup and Case Workspace</div>
    <div class='small text-muted mb-3'>Search for a customer, review their account, and use this panel as the starting point for service issues.</div>
    {results}
    {detailHtml}
  </div>
</div>";
        }

        private static string RenderAdminPointsManager(DataTable? customerDt, DataTable? pointsDt,
            string grain, string metricRange, int? locationId, string q, string adjAlert)
        {
            if (customerDt == null || customerDt.Rows.Count == 0)
            {
                return @"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>2. JabberWonk Points Manager</div>
    <div class='alert alert-light border mb-0'>Select a customer to inspect loyalty history and points behavior.</div>
  </div>
</div>";
            }

            DataRow customer = customerDt.Rows[0];
            int customerId = RowInt(customer, "CustomerID");
            string returnUrl = BuildAdminUrl(grain, locationId, q, customerId, null, metricRange);

            var history = new StringBuilder();
            if (pointsDt == null || pointsDt.Rows.Count == 0)
            {
                history.Append("<div class='small text-muted'>No loyalty transactions recorded.</div>");
            }
            else
            {
                history.Append("<div class='table-responsive'><table class='table table-sm mb-0'><thead><tr><th>Date</th><th>Type</th><th>Delta</th><th>Balance</th></tr></thead><tbody>");
                foreach (DataRow row in pointsDt.Rows)
                {
                    history.Append($@"
<tr>
  <td>{H(RowDate(row, "JWT_TransactionDate")?.ToString("MMM d, h:mm tt") ?? "")}</td>
  <td>{H(RowString(row, "JWT_TransactionType"))}</td>
  <td>{RowInt(row, "JWT_PointsDelta"):N0}</td>
  <td>{RowInt(row, "JWT_BalanceAfter"):N0}</td>
</tr>");
                }
                history.Append("</tbody></table></div>");
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>2. JabberWonk Points Manager</div>
    {adjAlert}
    <div class='d-flex justify-content-between align-items-center mb-3'>
      <div>
        <div class='fw-semibold'>{H(RowString(customer, "CUS_Username"))}</div>
        <div class='small text-muted'>Current loyalty balance</div>
      </div>
      <div class='fs-4 fw-bold text-warning-emphasis'>{RowInt(customer, "CUS_PointsBalance"):N0} pts</div>
    </div>
    <form method='post' action='/admin/points-adjust' class='subtle-box p-3 mb-3'>
      <div class='mini-label mb-2'>Manual Adjustment</div>
      <input type='hidden' name='customerId' value='{customerId}'>
      <input type='hidden' name='grain' value='{H(grain)}'>
      <input type='hidden' name='metricRange' value='{H(metricRange)}'>
      <input type='hidden' name='locationId' value='{(locationId.HasValue ? locationId.Value.ToString() : "")}'>
      <input type='hidden' name='q' value='{H(q)}'>
      <div class='row g-2 align-items-end'>
        <div class='col-sm-4'>
          <label class='form-label mini-label mb-1'>Delta (+ add / − deduct)</label>
          <input type='number' class='form-control form-control-sm' name='delta'
                 placeholder='e.g. 100 or -50' required>
        </div>
        <div class='col-sm-5'>
          <label class='form-label mini-label mb-1'>Reason</label>
          <input type='text' class='form-control form-control-sm' name='reason'
                 maxlength='100' placeholder='Brief reason for adjustment'>
        </div>
        <div class='col-sm-3'>
          <button type='submit' class='btn btn-warning btn-sm w-100 fw-semibold'>Adjust</button>
        </div>
      </div>
    </form>
    <div class='subtle-box p-3'>
      <div class='mini-label mb-2'>Recent Points Activity</div>
      {history}
    </div>
  </div>
</div>";
        }

        private static string RenderAdminLiveOrders(string grain, string metricRange, int? locationId, DataTable? liveOrdersDt)
        {
            var rows = new StringBuilder();
            if (liveOrdersDt == null || liveOrdersDt.Rows.Count == 0)
            {
                rows.Append("<tr><td colspan='5' class='text-muted'>No live orders found.</td></tr>");
            }
            else
            {
                foreach (DataRow row in liveOrdersDt.Rows)
                {
                    string orderLink = BuildAdminUrl(grain, locationId, null, RowInt(row, "CustomerID"), RowInt(row, "OrderID"), metricRange);
                    rows.Append($@"
<tr>
  <td><a href='{orderLink}'>#{RowInt(row, "OrderID")}</a></td>
  <td>{H(RowString(row, "Username"))}</td>
  <td>{H(RowString(row, "StoreName"))}</td>
  <td>{H(RowString(row, "ORD_Status"))}</td>
  <td>{RowInt(row, "AgeMinutes")} min</td>
</tr>");
                }
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>3. Live Order Queue</div>
    <div class='table-responsive'>
      <table class='table align-middle mb-0'>
        <thead><tr><th>Order</th><th>Customer</th><th>Location</th><th>Status</th><th>Age</th></tr></thead>
        <tbody>{rows}</tbody>
      </table>
    </div>
  </div>
</div>";
        }

        private static string RenderAdminSalesTrend(string grain, DataTable? salesTrendDt)
        {
            decimal maxSales = 0m;
            if (salesTrendDt != null)
                foreach (DataRow row in salesTrendDt.Rows)
                    maxSales = Math.Max(maxSales, RowDecimal(row, "Sales"));

            var rows = new StringBuilder();
            if (salesTrendDt == null || salesTrendDt.Rows.Count == 0)
            {
                rows.Append("<div class='text-muted'>No completed order data available for this trend view.</div>");
            }
            else
            {
                foreach (DataRow row in salesTrendDt.Rows)
                {
                    decimal sales = RowDecimal(row, "Sales");
                    decimal width = maxSales <= 0 ? 0 : Math.Round((sales / maxSales) * 100m, 1);
                    rows.Append($@"
<div class='mb-3'>
  <div class='d-flex justify-content-between small mb-1'>
    <span>{H(RowString(row, "BucketLabel"))}</span>
    <span>${sales:F2} / {RowInt(row, "OrderCount")} orders</span>
  </div>
  <div class='trend-bar' style='width:{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}%;'></div>
</div>");
                }
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>4. Sales Trend</div>
    <div class='small text-muted mb-3'>Current view: {H(grain.ToUpperInvariant())}</div>
    {rows}
  </div>
</div>";
        }

        private static string RenderAdminActivityFeed(DataTable? activityDt)
        {
            var items = new StringBuilder();
            if (activityDt == null || activityDt.Rows.Count == 0)
            {
                items.Append("<div class='text-muted'>No recent activity found.</div>");
            }
            else
            {
                foreach (DataRow row in activityDt.Rows)
                {
                    items.Append($@"
<div class='subtle-box p-3 mb-2'>
  <div class='activity-entry'>
    <div class='activity-time'>{H(RowDate(row, "ActivityAt")?.ToString("MMM d, h:mm tt") ?? "")}</div>
    <span class='badge text-bg-light activity-type'>{H(RowString(row, "ActivityType"))}</span>
    <div class='activity-title'>{H(RowString(row, "Title"))}</div>
    <div class='activity-detail'>{H(RowString(row, "Detail"))}</div>
  </div>
</div>");
                }
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='d-flex flex-wrap justify-content-between align-items-center gap-2 mb-3'>
      <div class='section-title mb-0'>5. Recent Activity Feed</div>
      <div class='small text-muted'>Newest activity first. Scroll for older entries.</div>
    </div>
    <div class='activity-scroll'>
      {items}
    </div>
  </div>
</div>";
        }

        private static string RenderAdminPopularItems(DataTable? popularItemsDt)
        {
            var rows = new StringBuilder();
            if (popularItemsDt == null || popularItemsDt.Rows.Count == 0)
            {
                rows.Append("<div class='text-muted'>No completed item sales found for this time window.</div>");
            }
            else
            {
                rows.Append("<div class='table-responsive'><table class='table table-sm mb-0'><thead><tr><th>Item</th><th>Units</th><th>Revenue</th></tr></thead><tbody>");
                foreach (DataRow row in popularItemsDt.Rows)
                {
                    rows.Append($@"
<tr>
  <td>{H(RowString(row, "ITM_ItemName"))}<div class='small text-muted'>{H(RowString(row, "ITM_Category"))}</div></td>
  <td>{RowInt(row, "UnitsSold")}</td>
  <td>${RowDecimal(row, "Revenue"):F2}</td>
</tr>");
                }
                rows.Append("</tbody></table></div>");
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>6. Most Popular Items</div>
    {rows}
  </div>
</div>";
        }

        private static string RenderAdminPaymentMix(DataTable? paymentMixDt, DataTable? pointsMixDt)
        {
            var paymentRows = new StringBuilder();
            if (paymentMixDt == null || paymentMixDt.Rows.Count == 0)
            {
                paymentRows.Append("<div class='text-muted'>No payment data available.</div>");
            }
            else
            {
                paymentRows.Append("<div class='table-responsive'><table class='table table-sm mb-3'><thead><tr><th>Type</th><th>Orders</th><th>Sales</th></tr></thead><tbody>");
                foreach (DataRow row in paymentMixDt.Rows)
                {
                    paymentRows.Append($@"
<tr>
  <td>{H(RowString(row, "PaymentType"))}</td>
  <td>{RowInt(row, "OrderCount")}</td>
  <td>${RowDecimal(row, "Sales"):F2}</td>
</tr>");
                }
                paymentRows.Append("</tbody></table></div>");
            }

            int earned = 0;
            int redeemed = 0;
            int refunded = 0;
            if (pointsMixDt != null && pointsMixDt.Rows.Count > 0)
            {
                var row = pointsMixDt.Rows[0];
                earned = RowInt(row, "PointsEarned");
                redeemed = RowInt(row, "PointsRedeemed");
                refunded = RowInt(row, "PointsRefunded");
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='section-title mb-3'>7. Payment and Loyalty Mix</div>
    {paymentRows}
    <div class='row g-2'>
      <div class='col-4'><div class='subtle-box p-2'><div class='mini-label'>Earned</div><div class='fw-bold'>{earned:N0}</div></div></div>
      <div class='col-4'><div class='subtle-box p-2'><div class='mini-label'>Redeemed</div><div class='fw-bold'>{redeemed:N0}</div></div></div>
      <div class='col-4'><div class='subtle-box p-2'><div class='mini-label'>Refunded</div><div class='fw-bold'>{refunded:N0}</div></div></div>
    </div>
  </div>
</div>";
        }

        private static string RenderAdminLocationPerformance(DataTable? locationPerfDt, bool isGlobal)
        {
            var rows = new StringBuilder();
            if (locationPerfDt == null || locationPerfDt.Rows.Count == 0)
            {
                rows.Append("<tr><td colspan='6' class='text-muted'>No location performance data available.</td></tr>");
            }
            else
            {
                foreach (DataRow row in locationPerfDt.Rows)
                {
                    int completed = RowInt(row, "CompletedOrders");
                    int cancelled = RowInt(row, "CancelledOrders");
                    decimal cancelRate = completed + cancelled == 0 ? 0m : Math.Round((decimal)cancelled * 100m / (completed + cancelled), 1);

                    rows.Append($@"
<tr>
  <td>{H(RowString(row, "StoreName"))}</td>
  <td>{completed}</td>
  <td>{RowInt(row, "PendingOrders")}</td>
  <td>{cancelRate:F1}%</td>
  <td>${RowDecimal(row, "Revenue"):F2}</td>
  <td>${RowDecimal(row, "AvgTicket"):F2}</td>
</tr>");
                }
            }

            return $@"
<div class='card admin-card'>
  <div class='card-body'>
    <div class='d-flex justify-content-between align-items-start gap-2 mb-3'>
      <div>
        <div class='section-title'>8. Store Performance by Location</div>
        <div class='small text-muted'>Revenue and operational output over the last 7 days. {(isGlobal ? "Global comparison view." : "Scoped to this admin's store access.")}</div>
      </div>
      <span class='badge text-bg-light'>Avg pickup completion time needs a dedicated completion timestamp</span>
    </div>
    <div class='table-responsive'>
      <table class='table align-middle mb-0'>
        <thead><tr><th>Location</th><th>Completed</th><th>Pending</th><th>Cancel %</th><th>Revenue</th><th>Avg Ticket</th></tr></thead>
        <tbody>{rows}</tbody>
      </table>
    </div>
  </div>
</div>";
        }

        // ─── DATABASE HELPERS ─────────────────────────────────────────────────

        private static string GetConnectionString() =>
            _config.GetConnectionString("JabberJuicy")
            ?? throw new InvalidOperationException("Connection string 'JabberJuicy' is not configured.");

        private static async Task<DataTable?> FillDataTableViaSqlAsync(string sql, ILogger? logger = null) =>
            await FillDataTableViaCommandAsync(new SqlCommand(sql), logger);

        private static async Task<DataTable?> FillDataTableViaCommandAsync(SqlCommand cmd, ILogger? logger = null)
        {
            const int MaxRetries = 10;
            int retry = 0;

            while (retry <= MaxRetries)
            {
                try
                {
                    await using SqlConnection conn = new SqlConnection(GetConnectionString());
                    await conn.OpenAsync();
                    cmd.Connection = conn;
                    cmd.CommandTimeout = 60;
                    await using SqlDataReader reader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                    DataTable table = new DataTable();
                    await Task.Run(() => table.Load(reader));
                    return table;
                }
                catch (SqlException ex) when (
                    retry < MaxRetries &&
                    (ex.Message.Contains("deadlock victim") ||
                     ex.Message.Contains("INSERT EXEC failed") ||
                     ex.Message.Contains("Schema changed") ||
                     ex.Message.Contains("The current transaction attempted to update a record that has been updated since this transaction started.")))
                {
                    await Task.Delay(3337);
                    retry++;
                }
                catch (Exception ex)
                {
                    if (logger != null)
                        logger.LogError(ex, "SQL command failed: {CommandText}", cmd.CommandText);
                    else
                        Console.WriteLine($"SQL command failed: {cmd.CommandText}\n{ex.Message}");
                    return null;
                }
            }
            return null;
        }

        private static async Task<bool> ExecSqlStringAsync(string sqlString, ILogger? logger = null) =>
            await ExecSqlCommandAsync(new SqlCommand(sqlString), logger);

        private static async Task<bool> ExecSqlCommandAsync(SqlCommand cmd, ILogger? logger = null)
        {
            const int MaxRetries = 2;
            int retry = 0;

            while (retry <= MaxRetries)
            {
                try
                {
                    await using SqlConnection conn = new SqlConnection(GetConnectionString());
                    await conn.OpenAsync();
                    cmd.Connection = conn;
                    cmd.CommandTimeout = 60;
                    await cmd.ExecuteNonQueryAsync();
                    return true;
                }
                catch (SqlException ex) when (
                    retry < MaxRetries &&
                    (ex.Message.Contains("deadlock victim") ||
                     ex.Message.Contains("INSERT EXEC failed") ||
                     ex.Message.Contains("Schema changed")))
                {
                    if (logger != null)
                        logger.LogWarning(ex, "Transient SQL error, retrying attempt {Retry}", retry + 1);
                    await Task.Delay(3337);
                    retry++;
                }
                catch (Exception ex)
                {
                    if (logger != null)
                        logger.LogError(ex, "SQL command failed: {CommandText}", cmd.CommandText);
                    else
                        Console.WriteLine($"SQL command failed: {cmd.CommandText}\n{ex.Message}");
                    return false;
                }
            }
            return false;
        }

        // ── Background service: auto-cancel Pending orders older than ORDER_EXPIRY_MINUTES ──
        private sealed class ExpiredOrderCleanupService : BackgroundService
        {
            private readonly ILogger<ExpiredOrderCleanupService> _logger;

            public ExpiredOrderCleanupService(ILogger<ExpiredOrderCleanupService> logger)
                => _logger = logger;

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation(
                    "Order expiry service started — checking every {Hours}h, expiry threshold {Min}min.",
                    ORDER_CLEANUP_INTERVAL.TotalHours, ORDER_EXPIRY_MINUTES);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try   { await RunCleanupAsync(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        // Log but never rethrow — an unhandled exception here kills the whole process
                        _logger.LogError(ex, "Unexpected error in order expiry cleanup.");
                    }

                    await Task.Delay(ORDER_CLEANUP_INTERVAL, stoppingToken);
                }

                _logger.LogInformation("Order expiry service stopped.");
            }

            private async Task RunCleanupAsync(CancellationToken ct)
            {
                var cmd = new SqlCommand(@"
                    SELECT OrderID, CustomerID FROM [Order]
                    WHERE  ORD_Status   = 'Pending'
                      AND  ORD_OrderDate < DATEADD(MINUTE, -@exp, GETDATE())");
                cmd.Parameters.AddWithValue("@exp", ORDER_EXPIRY_MINUTES);

                var dt = await FillDataTableViaCommandAsync(cmd, _logger);
                if (dt == null || dt.Rows.Count == 0) return;

                _logger.LogInformation("Expiry sweep: found {Count} expired pending order(s).", dt.Rows.Count);

                foreach (DataRow row in dt.Rows)
                {
                    if (ct.IsCancellationRequested) break;
                    int oid = Convert.ToInt32(row["OrderID"]);
                    int cid = Convert.ToInt32(row["CustomerID"]);
                    try
                    {
                        await CancelOrderWithRefundAsync(oid, cid, _logger,
                            $"AUTO-CANCELLED Order #{oid} — expired after {ORDER_EXPIRY_MINUTES} min");
                        _logger.LogInformation("Auto-cancelled expired Order #{OrderId}.", oid);
                    }
                    catch (Exception ex)
                    {
                        // One failing order must not block the rest
                        _logger.LogError(ex, "Failed to auto-cancel Order #{OrderId}.", oid);
                    }
                }
            }
        }
    }
}
