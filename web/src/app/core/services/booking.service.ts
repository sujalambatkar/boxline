import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { Booking, BookingListQuery, SaveBookingRequest } from '../models/booking.models';
import { PagedResponse } from '../models/container.models';

@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly url = `${environment.apiUrl}/bookings`;

  constructor(private readonly http: HttpClient) {}

  list(query: BookingListQuery): Observable<PagedResponse<Booking>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);

    if (query.search.trim()) {
      params = params.set('search', query.search.trim());
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.polId !== null) {
      params = params.set('polId', query.polId);
    }
    if (query.podId !== null) {
      params = params.set('podId', query.podId);
    }
    if (query.createdFrom) {
      params = params.set('createdFrom', query.createdFrom);
    }
    if (query.createdTo) {
      params = params.set('createdTo', query.createdTo);
    }

    return this.http.get<PagedResponse<Booking>>(this.url, { params });
  }

  get(id: number): Observable<Booking> {
    return this.http.get<Booking>(`${this.url}/${id}`);
  }

  create(request: SaveBookingRequest): Observable<Booking> {
    return this.http.post<Booking>(this.url, request);
  }

  update(id: number, request: SaveBookingRequest, rowVersion: string): Observable<Booking> {
    return this.http.put<Booking>(`${this.url}/${id}`, { ...request, rowVersion });
  }

  /** Puts a booking on a sailing, or takes it off with null. Ops only. */
  assignVoyage(id: number, voyageId: number | null): Observable<Booking> {
    return this.http.put<Booking>(`${this.url}/${id}/voyage`, { voyageId });
  }

  confirm(id: number): Observable<Booking> {
    return this.http.post<Booking>(`${this.url}/${id}/confirm`, null);
  }

  cancel(id: number): Observable<Booking> {
    return this.http.post<Booking>(`${this.url}/${id}/cancel`, null);
  }
}
