import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges } from '@angular/core';
import { catchError, of, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { BookingVoyage } from '../../../core/models/booking.models';
import { ScheduleOption } from '../../../core/models/voyage.models';
import { ScheduleService } from '../../../core/services/schedule.service';

interface SailingChoice {
  id: number;
  label: string;
}

/**
 * Lets the user pick a sailing for a route. It looks up open sailings whenever the loading or discharge
 * port changes, keeps the booking's current sailing selectable even if it is no longer on offer, and clears
 * a selection that does not fit a newly chosen route.
 */
@Component({
  selector: 'app-sailing-select',
  templateUrl: './sailing-select.component.html',
  styleUrls: ['./sailing-select.component.scss'],
})
export class SailingSelectComponent implements OnChanges, OnDestroy {
  @Input() polId: number | null = null;
  @Input() podId: number | null = null;
  /** The selected voyage id, or null for "no sailing yet". */
  @Input() value: number | null = null;
  /** The sailing the booking already has, shown even when it is not among the current search results. */
  @Input() current: BookingVoyage | null = null;
  @Input() disabled = false;
  @Output() valueChange = new EventEmitter<number | null>();

  choices: SailingChoice[] = [];
  loading = false;
  loaded = false;
  failed = false;

  private readonly lookup$ = new Subject<[number, number] | null>();
  private readonly destroy$ = new Subject<void>();
  private options: ScheduleOption[] = [];

  constructor(private readonly schedules: ScheduleService) {
    this.lookup$
      .pipe(
        tap(route => {
          this.loading = route !== null;
          this.failed = false;
          if (route === null) {
            this.options = [];
            this.loaded = false;
            this.rebuild();
          }
        }),
        switchMap(route =>
          route === null
            ? of(null)
            : this.schedules.search(route[0], route[1]).pipe(
                catchError(() => {
                  this.failed = true;
                  return of([] as ScheduleOption[]);
                }),
              ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(options => {
        if (options === null) {
          return;
        }
        this.options = options;
        this.loading = false;
        this.loaded = true;
        this.rebuild();
        this.dropSelectionThatNoLongerFits();
      });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['polId'] || changes['podId']) {
      const ready = this.polId !== null && this.podId !== null && this.polId !== this.podId;
      this.lookup$.next(ready ? [this.polId as number, this.podId as number] : null);
    } else if (changes['current'] || changes['value']) {
      this.rebuild();
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  select(id: number | null): void {
    this.valueChange.emit(id);
  }

  get hasRoute(): boolean {
    return this.polId !== null && this.podId !== null && this.polId !== this.podId;
  }

  private rebuild(): void {
    const choices = this.options.map(o => ({ id: o.voyageId, label: SailingSelectComponent.describe(o) }));

    if (this.current && !choices.some(c => c.id === this.current!.id)) {
      choices.unshift({ id: this.current.id, label: `${this.current.vesselName} · ${this.current.voyageNumber} (current sailing)` });
    }
    this.choices = choices;
  }

  /** After a new route is loaded, a previously chosen voyage that does not serve it is cleared. */
  private dropSelectionThatNoLongerFits(): void {
    if (this.value !== null && !this.choices.some(c => c.id === this.value)) {
      this.valueChange.emit(null);
    }
  }

  private static describe(o: ScheduleOption): string {
    const day = (iso: string) =>
      new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', timeZone: 'UTC' });
    return `${o.vesselName} · ${o.voyageNumber}: departs ${day(o.departure)}, arrives ${day(o.arrival)} (${o.transitDays} days)`;
  }
}
