import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PageEvent } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  EMPTY,
  filter,
  merge,
  startWith,
  Subject,
  switchMap,
  takeUntil,
  tap,
} from 'rxjs';

import {
  Container,
  ContainerListQuery,
  ContainerSortField,
  ContainerStatus,
  ContainerType,
  OWNERSHIP_LABELS,
  STATUS_LABELS,
} from '../../core/models/container.models';
import { ContainerTypeService } from '../../core/services/container-type.service';
import { ContainerService } from '../../core/services/container.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog/confirm-dialog.component';
import { ContainerDialogComponent, ContainerDialogData } from './container-dialog/container-dialog.component';

@Component({
  selector: 'app-containers',
  templateUrl: './containers.component.html',
  styleUrls: ['./containers.component.scss'],
})
export class ContainersComponent implements OnInit, OnDestroy {
  readonly columns = ['number', 'type', 'ownership', 'status', 'actions'] as const;
  readonly ownershipLabels = OWNERSHIP_LABELS;
  readonly statusLabels = STATUS_LABELS;
  readonly statuses = Object.keys(STATUS_LABELS) as ContainerStatus[];
  readonly pageSizeOptions = [10, 20, 50];

  readonly search = new FormControl('', { nonNullable: true });
  readonly typeFilter = new FormControl<number | null>(null);
  readonly statusFilter = new FormControl<ContainerStatus | ''>('', { nonNullable: true });

  containers: Container[] = [];
  types: ContainerType[] = [];
  totalCount = 0;
  loading = false;

  pageIndex = 0;
  pageSize = 20;
  sortBy: ContainerSortField = 'number';
  sortDirection: 'asc' | 'desc' = 'asc';

  private readonly reload$ = new Subject<void>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly containerService: ContainerService,
    private readonly typeService: ContainerTypeService,
    private readonly dialog: MatDialog,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.typeService.list().pipe(takeUntil(this.destroy$)).subscribe(types => (this.types = types));

    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.typeFilter.valueChanges,
      this.statusFilter.valueChanges,
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
          this.containerService.list(this.currentQuery()).pipe(
            catchError(() => {
              this.loading = false;
              this.snackBar.open('Could not load containers. Is the API running?', 'Dismiss', { duration: 6000 });
              return EMPTY;
            }),
          ),
        ),
        takeUntil(this.destroy$),
      )
      .subscribe(result => {
        this.containers = result.items;
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

  onSort(sort: Sort): void {
    this.sortBy = (sort.direction ? sort.active : 'number') as ContainerSortField;
    this.sortDirection = sort.direction === 'desc' ? 'desc' : 'asc';
    this.pageIndex = 0;
    this.reload$.next();
  }

  clearFilters(): void {
    this.search.setValue('');
    this.typeFilter.setValue(null);
    this.statusFilter.setValue('');
  }

  get hasFilters(): boolean {
    return !!this.search.value || this.typeFilter.value !== null || !!this.statusFilter.value;
  }

  add(): void {
    this.openDialog({ types: this.types });
  }

  edit(container: Container): void {
    this.openDialog({ types: this.types, container });
  }

  remove(container: Container): void {
    this.dialog
      .open(ConfirmDialogComponent, {
        data: {
          title: 'Delete container',
          message: `Delete container ${container.number}? This cannot be undone.`,
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .pipe(
        filter(Boolean),
        switchMap(() => this.containerService.delete(container.id)),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: () => {
          this.snackBar.open(`Container ${container.number} deleted.`, undefined, { duration: 4000 });
          this.reload$.next();
        },
        error: (error: HttpErrorResponse) =>
          this.snackBar.open(error.error?.detail ?? 'Could not delete the container.', 'Dismiss', { duration: 6000 }),
      });
  }

  ownershipLabel(container: Container): string {
    return this.ownershipLabels[container.ownershipType];
  }

  statusLabel(container: Container): string {
    return this.statusLabels[container.status];
  }

  trackById(_: number, container: Container): number {
    return container.id;
  }

  private openDialog(data: ContainerDialogData): void {
    this.dialog
      .open<ContainerDialogComponent, ContainerDialogData, Container>(ContainerDialogComponent, {
        data,
        width: '480px',
      })
      .afterClosed()
      .pipe(filter((saved): saved is Container => !!saved), takeUntil(this.destroy$))
      .subscribe(saved => {
        this.snackBar.open(`Container ${saved.number} saved.`, undefined, { duration: 4000 });
        this.reload$.next();
      });
  }

  private currentQuery(): ContainerListQuery {
    return {
      search: this.search.value,
      containerTypeId: this.typeFilter.value,
      status: this.statusFilter.value,
      sortBy: this.sortBy,
      sortDirection: this.sortDirection,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    };
  }
}
