# Agent Instructions for SizerDataCollector

## Operational event workflows

- Use `LOT_TRANSITION_WORKFLOW.md` for grower lot or batch changeover disruption. Those events live in `oee.lot_transition_throughput_events` and should be described as transition throughput opportunity loss, not literal waste.
- Use `MACHINE_EVENT_WORKFLOW.md` for general machine downtime and non-transition slowdown detection. Those events live in `oee.downtime_events` and `oee.slowdown_events`.
- Use `SHIFT_SETUP_WORKFLOW.md` when defining shift boundaries or validating shift-window rollups. Shift definitions live in `oee.shifts`, and shift summaries are exposed through `oee.v_shift_window`, `oee.v_oee_shift_batch`, and `oee.v_oee_shift`.
- Before reporting downtime or slowdown conclusions, check whether lot-transition exclusion is enabled (`MachineEventExcludeLotTransitions`) and whether any event has `overlaps_lot_transition = true`.
- Prefer the reporting views `oee.v_machine_event_detail`, `oee.v_downtime_event_detail`, `oee.v_slowdown_event_detail`, and `oee.v_operational_minute_batch` over querying raw event tables directly unless you are validating persistence behavior.
- For tuning guidance, cite the CLI commands in `MACHINE_EVENT_WORKFLOW.md` and remind operators to restart the Windows service after `set-machine-events` changes so the background loop reloads runtime settings.

## Validation guidance

- Treat event tables as model output. Validate unusual or high-impact results against `oee.v_operational_minute_batch` and, when necessary, raw `public.metrics`.
- Keep lot-transition, downtime, slowdown, grade anomaly, and size anomaly reports separate unless the user explicitly asks for a combined operational-impact view.
