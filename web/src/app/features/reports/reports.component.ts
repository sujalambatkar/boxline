import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { PageEvent } from '@angular/material/paginator';
import { catchError, debounceTime, distinctUntilChanged, merge, of, startWith, Subject, switchMap, takeUntil, tap } from 'rxjs';

import { Port } from '../../core/models/booking.models';
import { UtilisationQuery, UtilisationReport } from '../../core/models/report.models';
import { UN_LOCODE_PATTERN } from '../../core/models/tracking.models';
import { PortService } from '../../core/services/port.service';
import { ReportService } from '../../core/services/report.service';
import { trimmedPattern } from '../../shared/validators/trimmed-pattern';

/**
 * Container utilisation by month and location. With no month range the server reports the latest twelve months; the table is
 * paged on the server and the chart shows the monthly totals of the whole filter, not just the current page.
 */
@Component({
  selector: 'app-reports',
  templateUrl: './reports.component.html',
  styleUrls: ['./reports.component.scss'],
})
export class ReportsComponent implements OnInit, OnDestroy {
  readonly columns = ['month', 'location', 'port', 'movements', 'containers', 'loaded', 'discharged', 'utilisation'] as const;
  readonly pageSizeOptions = [10, 20, 50, 100];

  readonly fromMonth = new FormControl('', { nonNullable: true });
  readonly toMonth = new FormControl('', { nonNullable: true });
  readonly location = new FormControl('', { nonNullable: true, validators: trimmedPattern(UN_LOCODE_PATTERN) });

  report: UtilisationReport | null = null;
  ports: Port[] = [];
  loading = false;
  message: string | null = null;
  pageIndex = 0;
  pageSize = 20;

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly reports: ReportService,
    private readonly portService: PortService,
  ) {}

  ngOnInit(): void {
    this.portService.list().pipe(takeUntil(this.destroy$)).subscribe(ports => (this.ports = ports));

    merge(
      this.fromMonth.valueChanges,
      this.toMonth.valueChanges,
      this.location.valueChanges.pipe(debounceTime(400), distinctUntilChanged()),
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.pageIndex = 0;
        this.reload$.next();
      });

    this.reload$
      .pipe(
        startWith(undefined),
        // An invalid location waits until it is corrected instead of asking the server a question it will refuse.
        switchMap(() => {
          if (this.location.invalid) {
            return of(null);
          }
          this.loading = true;
          this.message = null;
          return this.reports.utilisation(this.currentQuery()).pipe(
            catchError((error: HttpErrorResponse) => {
              this.message = ReportsComponent.describe(error);
              return of(null);
            }),
          );
        }),
        tap(() => (this.loading = false)),
        takeUntil(this.destroy$),
      )
      .subscribe(report => {
        if (report) {
          this.report = report;
        }
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

  clearFilters(): void {
    this.fromMonth.setValue('');
    this.toMonth.setValue('');
    this.location.setValue('');
  }

  get hasFilters(): boolean {
    return !!this.fromMonth.value || !!this.toMonth.value || !!this.location.value.trim();
  }

  /** True when no month range is chosen, so the server applies its default. */
  get usingDefaultRange(): boolean {
    return !this.fromMonth.value && !this.toMonth.value;
  }

  /** Width (percent of the cell) of the little bar next to each utilisation figure. */
  barWidth(percent: number): number {
    return Math.min(100, Math.max(0, percent));
  }

  trackByRow(_: number, row: { month: string; location: string }): string {
    return `${row.month}|${row.location}`;
  }

  private currentQuery(): UtilisationQuery {
    return {
      fromMonth: this.fromMonth.value,
      toMonth: this.toMonth.value,
      location: this.location.value,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
  }

  private static describe(error: HttpErrorResponse): string {
    if (error.status === 400 && error.error?.errors) {
      return (Object.values(error.error.errors) as string[][]).flat().join(' ');
    }
    return 'The report could not be loaded. Is the API running?';
  }
}
