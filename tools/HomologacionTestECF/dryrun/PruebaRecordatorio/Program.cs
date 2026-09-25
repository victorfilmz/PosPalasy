// Prueba de aislamiento de los umbrales del recordatorio del certificado (doc 21 §4).
// Replica EXACTAMENTE la aritmética del recordatorio_certificado.cmd
// (if DIAS LEQ 7 / 15 / 30 / 60 con TotalDays truncado a int) contra fechas límite.
//
// Invariante clave: el truncado de TotalDays (AddDays(N) ⇒ N-1 días enteros) hace que cada
// recordatorio dispare como máximo UN DÍA ANTES del umbral nominal — la dirección segura.
// Lo que NUNCA debe pasar es que un recordatorio dispare MÁS TARDE que su fase.
using System.Globalization;

int DiasRestantes(DateTimeOffset vence) => (int)(vence - DateTimeOffset.Now).TotalDays;

// 0=OK, 1=D-60, 2=D-30, 3=D-15, 4=D-7 — urgencia creciente.
int Urgencia(int dias) => dias switch
{
    <= 7 => 4,
    <= 15 => 3,
    <= 30 => 2,
    <= 60 => 1,
    _ => 0
};

string Nombre(int urgencia) => urgencia switch
{
    4 => "Id 103 ultima barrera (D-7)",
    3 => "Id 102 verificacion en profundidad (D-15)",
    2 => "Id 101 D-30",
    1 => "Id 100 D-60",
    _ => "Id 200 OK"
};

// (vence, urgenciaNominal, nota): a qué fase pertenece nominalmente la fecha.
var casos = new (DateTimeOffset Vence, int Nominal, string Nota)[]
{
    (DateTimeOffset.Now.AddDays(90),   0, "lejos del vencimiento"),
    (DateTimeOffset.Now.AddDays(61),   0, "fuera del umbral de 60"),
    (DateTimeOffset.Now.AddDays(60),   1, "borde nominal de 60 (dispara 1 dia antes: seguro)"),
    (DateTimeOffset.Now.AddDays(45),   1, "mitad de la fase D-60"),
    (DateTimeOffset.Now.AddDays(31),   1, "un dia antes del borde de 30"),
    (DateTimeOffset.Now.AddDays(30),   2, "borde nominal de 30 (dispara 1 dia antes: seguro)"),
    (DateTimeOffset.Now.AddDays(16),   2, "un dia antes del borde de 15"),
    (DateTimeOffset.Now.AddDays(15),   3, "borde nominal de 15 (dispara 1 dia antes: seguro)"),
    (DateTimeOffset.Now.AddDays(8),    3, "un dia antes del borde de 7"),
    (DateTimeOffset.Now.AddDays(7),    4, "borde nominal de 7 (dispara 1 dia antes: seguro)"),
    (DateTimeOffset.Now.AddDays(1),    4, "manana"),
    (DateTimeOffset.Now.AddDays(0.9),  4, "hoy mismo"),
    (DateTimeOffset.Now.AddDays(-1),   4, "YA VENCIDO (/health tambien lo reporta como Degraded)"),
};

var fallos = 0;
foreach (var (vence, nominal, nota) in casos)
{
    var dias = DiasRestantes(vence);
    var urgencia = Urgencia(dias);
    // PASS si dispara con la urgencia nominal O superior (más temprano = seguro).
    // FALLO solo si dispara con MENOS urgencia de la que corresponde (más tarde = peligro).
    var ok = urgencia >= nominal;
    if (!ok) fallos++;
    Console.WriteLine($"[{(ok ? "PASS" : "FALLO")}] dias={dias,3} → {Nombre(urgencia)}  (fase nominal: {Nombre(nominal)} | {nota})");
}

// Monotonía: a menos días restantes, urgencia igual o mayor (nunca regresa).
var monotona = true;
for (var d = 95; d >= -5; d--)
{
    if (Urgencia(d - 1) < Urgencia(d)) { monotona = false; Console.WriteLine($"[FALLO] la urgencia bajó de {d} a {d - 1} días"); }
}
Console.WriteLine(monotona ? "[PASS] urgencia monótona en todo el rango 95..-5 días" : "[FALLO] urgencia no monótona");
if (!monotona) fallos++;

Console.WriteLine();
Console.WriteLine(fallos == 0
    ? "UMBALES CORRECTOS: los recordatorios disparan a tiempo o un día antes (nunca después)."
    : $"{fallos} PROBLEMAS DE UMBRAL");
return fallos == 0 ? 0 : 1;
