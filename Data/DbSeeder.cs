using HorseVillage.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HorseVillage.Data;

public static class DbSeeder
{
    // Creates the first reporter account from Seed:AdminUsername / Seed:AdminPassword when the users table is empty.
    // With Seed:SampleData = true it also loads the demo users and plots shown in the UI prototype.
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher<User>>();

        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrEmpty(password) || await db.Users.AnyAsync()) return;

        var now = DateTimeOffset.UtcNow;
        User NewUser(string username, string fullName, UserRole role, bool active = true)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                FullName = fullName,
                Role = role,
                IsActive = active,
                RevokedAt = active ? null : now,
                CreatedAt = now
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            return user;
        }

        var admin = NewUser(configuration["Seed:AdminUsername"] ?? "reporter", "مقرر اللجنة الدائمة", UserRole.SUPER_ADMIN_REPORTER);
        db.Users.Add(admin);

        if (configuration.GetValue<bool>("Seed:SampleData"))
        {
            db.Users.AddRange(
                NewUser("president", "سعادة رئيس اللجنة الدائمة", UserRole.PRESIDENT),
                NewUser("engineer", "المهندس / مدير إدارة التراخيص", UserRole.ENGINEER_INSPECTOR),
                NewUser("former", "العضو السابق للجنة (منقول)", UserRole.ENGINEER_INSPECTOR, active: false));
            db.Plots.AddRange(SamplePlots(admin.Id, now));
        }

        await db.SaveChangesAsync();
    }

    private static IEnumerable<Plot> SamplePlots(Guid adminId, DateTimeOffset now)
    {
        Plot New(int serial, string beneficiary, string lottery, string contractNo, string expiry, string stud, string plotNo,
            string contact, string permits, string requests, string letters, string notes, PlotStatus status) => new()
        {
            Id = Guid.NewGuid(),
            SerialNo = serial,
            BeneficiaryName = beneficiary,
            LotteryDate = DateOnly.Parse(lottery),
            ContractNo = contractNo,
            ContractExpiryDate = DateOnly.Parse(expiry),
            StudName = stud,
            PlotNo = plotNo,
            ContactInfo = contact,
            BuildingPermits = permits,
            RequestsAndDecisions = requests,
            OfficialLetters = letters,
            Notes = notes,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedById = adminId
        };

        yield return New(1, "الشيخ ناصر بن حمد آل ثاني", "2023-03-15", "CNT-2023-001", "2028-03-14", "مربط الشقب الأصيل", "A-001",
            "55123456 / stud1@alkhor.qa",
            "رخصة بناء رقم 458/2023 معتمدة، إتمام بناء بنسبة 100%، تم إطلاق التيار الكهربائي.",
            "طلب اعتماد مخطط إسطبلات إضافية وتوسعة مضمار التدريب - تمت الموافقة.",
            "كتاب شكر وإفادة بالتزام الشروط رقم 12/2024.",
            "أحد النماذج المتميزة في قرية الخيول، جاهز للتشغيل الكامل.", PlotStatus.COMPLETED);
        yield return New(2, "سلطان مبارك الكواري", "2023-04-10", "CNT-2023-045", "2028-04-09", "مربط العقدة النموذجي", "B-014",
            "66998877 / sultan@domain.qa",
            "تم استخراج رخصة البناء رقم 892/2023، نسبة الإنجاز 75% وجاري استكمال التشطيبات.",
            "طلب تمديد مهلة البناء 3 أشهر لظروف توريد الأعمدة الخشبية.",
            "صادر رقم 114/2024 بالموافقة على تمديد مهلة الإتمام حتى يونيو 2026.",
            "منتظم في الحضور وسداد الرسوم المقررة.", PlotStatus.ACTIVE);
        yield return New(3, "محمد راشد الهاجري", "2023-05-02", "CNT-2023-088", "2025-05-01", "مربط الصافنات للفروسية", "C-032",
            "55443322 / m.hajri@horses.qa",
            "لم يتم البدء في أعمال الحفر، انتهاء المهلة الأولى دون تقديم مخططات هندسية.",
            "طلب إعادة جدولة المهلة.",
            "إنذار أول رقم 402/2025، إنذار أخير رقم 510/2025، وقرار لجنة بسحب الأرض وإلغاء التخصيص.",
            "الأرض مسحوبة بقرار اللجنة رقم 8/2026 وتم استرجاعها لحوزة المشروع.", PlotStatus.WITHDRAWN);
        yield return New(4, "غانم جاسم المهندي", "2023-06-20", "CNT-2023-112", "2028-06-19", "مربط الكحيلة التراثي", "A-055",
            "77001122 / ghanem@studs.qa",
            "رخصة بناء قيد المراجعة في بلدية الخور والذخيرة، فحص التربة معتمد.",
            "طلب تصريح حفر وسياج أولي مؤقت.",
            "كتاب إحالة إلى الإدارة الفنية للتراخيص برقم 990/2026.",
            "في انتظار إشعار البلدية للبدء في الهيكل الخرساني.", PlotStatus.IN_PROGRESS);
        yield return New(5, "سعد عيسى الرميحي", "2023-07-12", "CNT-2023-140", "2028-07-11", "مربط الريان الأصيل", "D-008",
            "55331188 / saad@alromaihi.qa",
            "رخصة بناء رقم 102/2024، الأساسات مكتملة.",
            "معاملة تبديل قسيمة برقم قرار 14/2026 مع المنتفع خليفة النعيمي.",
            "محضر تبادل وتنازل رقم 77/2026 معتمد من رئيس ومقرر اللجنة الدائمة.",
            "تم نقل القسيمة من D-022 إلى D-008 بموافقة الطرفين.", PlotStatus.SWAPPED);
        yield return New(6, "عبدالله حسن المناعي", "2023-08-01", "CNT-2023-195", "2028-07-31", "مربط النوايف لجمال الخيل", "B-080",
            "33887766 / mannai@stallions.qa",
            "شهادة إتمام بناء رقم 662/2024، محطة كهرباء فرعية منتهية.",
            "طلب فحص بيطري واعتماد إسطبل حجر صحي خاص.",
            "مخاطبة وزارة البلدية والبيئة برقم صادر 312/2025.",
            "جاهز للمشاركة في بطولات جمال الخيل القادمة.", PlotStatus.COMPLETED);
    }
}
