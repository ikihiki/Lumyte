using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed record PreparedOperation(int Index, PlanPass Pass, ArgumentTableReservation Reservation, PlanningShaderResources Resources, bool BindsTable, bool Graphics);
