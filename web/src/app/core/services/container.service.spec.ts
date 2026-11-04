import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ContainerListQuery } from '../models/container.models';
import { ContainerService } from './container.service';

describe('ContainerService', () => {
  let service: ContainerService;
  let http: HttpTestingController;

  const baseQuery: ContainerListQuery = {
    search: '',
    containerTypeId: null,
    status: '',
    sortBy: 'number',
    sortDirection: 'asc',
    page: 1,
    pageSize: 20,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ContainerService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only paging and sorting when no filters are set', () => {
    service.list(baseQuery).subscribe();

    const request = http.expectOne(r => r.url.endsWith('/containers'));
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize', 'sortBy', 'sortDirection']);
    request.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('sends trimmed search and active filters', () => {
    service.list({ ...baseQuery, search: '  CSQU  ', containerTypeId: 3, status: 'InUse', sortBy: 'type', sortDirection: 'desc', page: 2 }).subscribe();

    const params = http.expectOne(r => r.url.endsWith('/containers')).request.params;
    expect(params.get('search')).toBe('CSQU');
    expect(params.get('containerTypeId')).toBe('3');
    expect(params.get('status')).toBe('InUse');
    expect(params.get('sortBy')).toBe('type');
    expect(params.get('sortDirection')).toBe('desc');
    expect(params.get('page')).toBe('2');
  });
});
