import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, map, Observable, of, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { BOOKING_STATUS_LABELS } from '../../core/models/booking.models';
import { BookingTracking, ContainerTracking, isBookingNumber, MOVEMENT_EVENT_LABELS } from '../../core/models/tracking.models';
import { AuthService } from '../../core/services/auth.service';
import { TrackingService } from '../../core/services/tracking.service';
import { normalizeContainerNumber, validateContainerNumber } from '../../shared/validators/iso6346';

type Result = { kind: 'container'; data: ContainerTracking } | { kind: 'booking'; data: BookingTracking };

/**
 * Track a container (by ISO 6346 number) or a booking (by booking number). The query lives in the URL
 * (?q=...), so results can be bookmarked, shared and reached from other pages.
 */
@Component({
  selector: 'app-tracking',
  templateUrl: './tracking.component.html',
  styleUrls: ['./tracking.component.scss'],
})
export class TrackingComponent implements OnInit, OnDestroy {
  readonly isOps = this.auth.hasRole('Ops');
  readonly statusLabels = BOOKING_STATUS_LABELS;
  readonly eventLabels = MOVEMENT_EVENT_LABELS;
  readonly query = new FormControl('', { nonNullable: true });

  result: Result | null = null;
  loading = false;
  message: string | null = null;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly tracking: TrackingService,
    private readonly auth: AuthService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.route.queryParamMap
      .pipe(
        map(params => (params.get('q') ?? '').trim()),
        tap(q => {
          this.query.setValue(q, { emitEvent: false });
          this.message = null;
          if (!q) {
            this.result = null;
          }
        }),
        switchMap(q => this.lookup(q)),
        takeUntil(this.destroy$),
      )
      .subscribe();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Puts the query in the URL; the route subscription then performs the lookup. */
  search(): void {
    const q = this.query.value.trim();
    void this.router.navigate([], { queryParams: { q: q || null }, queryParamsHandling: 'merge' });
  }

  /** Re-runs the current lookup, e.g. after an event was recorded. */
  refresh(): void {
    this.lookup(this.query.value.trim()).pipe(takeUntil(this.destroy$)).subscribe();
  }

  open(containerNumber: string): void {
    void this.router.navigate([], { queryParams: { q: containerNumber } });
  }

  get container(): ContainerTracking | null {
    return this.result?.kind === 'container' ? this.result.data : null;
  }

  get booking(): BookingTracking | null {
    return this.result?.kind === 'booking' ? this.result.data : null;
  }

  private lookup(q: string): Observable<unknown> {
    if (!q) {
      return of(null);
    }

    if (!isBookingNumber(q)) {
      const problem = validateContainerNumber(q);
      if (problem) {
        this.result = null;
        this.message = `${problem} Booking numbers look like BL-202611-0001.`;
        return of(null);
      }
    }

    this.loading = true;
    const request$: Observable<Result> = isBookingNumber(q)
      ? this.tracking.booking(q.toUpperCase()).pipe(map(data => ({ kind: 'booking', data }) as Result))
      : this.tracking.container(normalizeContainerNumber(q)).pipe(map(data => ({ kind: 'container', data }) as Result));

    return request$.pipe(
      tap(result => {
        this.result = result;
        this.message = null;
        this.loading = false;
      }),
      catchError((error: HttpErrorResponse) => {
        this.result = null;
        this.loading = false;
        this.message =
          error.status === 404
            ? `Nothing found for ${q}. Check the number, or the booking may belong to another shipper.`
            : 'Tracking is unavailable right now. Please try again.';
        return of(null);
      }),
    );
  }
}
