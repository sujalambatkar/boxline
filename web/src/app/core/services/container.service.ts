import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import {
  Container,
  ContainerListQuery,
  CreateContainerRequest,
  PagedResponse,
  UpdateContainerRequest,
} from '../models/container.models';

@Injectable({ providedIn: 'root' })
export class ContainerService {
  private readonly url = `${environment.apiUrl}/containers`;

  constructor(private readonly http: HttpClient) {}

  list(query: ContainerListQuery): Observable<PagedResponse<Container>> {
    let params = new HttpParams()
      .set('sortBy', query.sortBy)
      .set('sortDirection', query.sortDirection)
      .set('page', query.page)
      .set('pageSize', query.pageSize);

    if (query.search.trim()) {
      params = params.set('search', query.search.trim());
    }
    if (query.containerTypeId !== null) {
      params = params.set('containerTypeId', query.containerTypeId);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }

    return this.http.get<PagedResponse<Container>>(this.url, { params });
  }

  create(request: CreateContainerRequest): Observable<Container> {
    return this.http.post<Container>(this.url, request);
  }

  update(id: number, request: UpdateContainerRequest): Observable<Container> {
    return this.http.put<Container>(`${this.url}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
