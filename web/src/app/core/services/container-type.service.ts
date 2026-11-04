import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ContainerType } from '../models/container.models';

@Injectable({ providedIn: 'root' })
export class ContainerTypeService {
  private readonly url = `${environment.apiUrl}/container-types`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<ContainerType[]> {
    return this.http.get<ContainerType[]>(this.url);
  }
}
