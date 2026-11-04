import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageEvent } from '@angular/material/paginator';
import { Router } from '@angular/router';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  EMPTY,
  merge,
  startWith,
  Subject,
  switchMap,
  takeUntil,
  tap,
} from 'rxjs';

import {
  Booking,
  BOOKING_STATUS_LABELS,
  BookingListQuery,
  BookingStatus,
  Port,
} from '../../core/models/booking.models';
import { AuthService } from '../../core/services/auth.service';
import { BookingService } from '../../core/services/booking.service';
import { FileDownloadService } from '../../core/services/file-download.service';
import { PortService } from '../../core/services/port.service';

@Component({
  selector: 'app-bookings',
  templateUrl: './bookings.component.html',
  styleUrls: ['./bookings.component.scss'],
})
export class BookingsComponent implements OnInit, OnDestroy {
  readonly statuses = Object.keys(BOOKING_STATUS_LABELS) as BookingStatus[];
  readonly statusLabels = BOOKING_STATUS_LABELS;
  readonly pageSizeOptions = [10, 20, 50];
  readonly isOps = this.auth.hasRole('Ops');
  readonly canCreate = this.auth.hasRole('Shipper');
  readonly columns = this.isOps
    ? ['number', 'shipper', 'route', 'voyage', 'containers', 'status', 'created']
    : ['number', 'route', 'voyage', 'containers', 'status', 'created'];

  readonly search = new FormControl('', { nonNullable: true });
  readonly statusFilter = new FormControl<BookingStatus | ''>('', { nonNullable: true });
  readonly polFilter = new FormControl<number | null>(null);
  readonly podFilter = new FormControl<number | null>(null);
  readonly fromFilter = new FormControl('', { nonNullable: true });
  readonly toFilter = new FormControl('', { nonNullable: true });

  bookings: Booking[] = [];
  ports: Port[] = [];
  totalCount = 0;
  loading = false;
  exporting = false;
  pageIndex = 0;
  pageSize = 20;

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly bookingService: BookingService,
    private readonly portService: PortService,
    private readonly auth: AuthService,
    private readonly router: Router,
    private readonly snackBar: MatSnackBar,
    private readonly files: FileDownloadService,
  ) {}

  ngOnInit(): void {
    this.portService.list().pipe(takeUntil(this.destroy$)).subscribe(ports => (this.ports = ports));

    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.statusFilter.valueChanges,
      this.polFilter.valueChanges,
      this.podFilter.valueChanges,
      this.fromFilter.valueChanges,
      this.toFilter.valueChanges,
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.pageIndex = 0;
        this.reload$.next();
      });

    this.reload$
      .pipe(
        startWith(undefined),
        tap(() => (this.loading = true)),
        switchMap(() =>
          this.bookingService.list(this.currentQuery()).pipe(
            catchError(() => {
              this.loading = false;
              this.snackBar.open('Could not load bookings. Is the API running?', 'Dismiss', { duration: 6000 });
              return EMPTY;
            }),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(result => {
        this.bookings = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload$.next();
  }

  open(booking: Booking): void {
    void this.router.navigate(['/bookings', booking.id]);
  }

  clearFilters(): void {
    this.search.setValue('');
    this.statusFilter.setValue('');
    this.polFilter.setValue(null);
    this.podFilter.setValue(null);
    this.fromFilter.setValue('');
    this.toFilter.setValue('');
  }

  get hasFilters(): boolean {
    return (
      !!this.search.value ||
      !!this.statusFilter.value ||
      this.polFilter.value !== null ||
      this.podFilter.value !== null ||
      !!this.fromFilter.value ||
      !!this.toFilter.value
    );
  }

  /** Downloads every booking that matches the current filters (not just this page). */
  export(format: 'csv' | 'xlsx'): void {
    if (this.exporting) {
      return;
    }

    this.exporting = true;
    this.bookingService.export(this.currentQuery(), format).subscribe({
      next: response => {
        this.exporting = false;
        this.files.save(response, `bookings.${format}`);
      },
      error: async (error: unknown) => {
        this.exporting = false;
        const message = await this.files.problemMessage(error, 'The export failed. Please try again.');
        this.snackBar.open(message, 'Dismiss', { duration: 6000 });
      },
    });
  }

  statusLabel(booking: Booking): string {
    return this.statusLabels[booking.status];
  }

  trackById(_: number, booking: Booking): number {
    return booking.id;
  }

  private currentQuery(): BookingListQuery {
    return {
      search: this.search.value,
      status: this.statusFilter.value,
      polId: this.polFilter.value,
      podId: this.podFilter.value,
      createdFrom: this.fromFilter.value,
      createdTo: this.toFilter.value,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
  }
}
