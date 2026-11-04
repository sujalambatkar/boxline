export type OwnershipType = 'Owned' | 'Leased';
export type ContainerStatus = 'Available' | 'InUse' | 'UnderRepair';
export type ContainerSortField = 'number' | 'type' | 'ownership' | 'status';

export interface ContainerType {
  id: number;
  code: string;
  lengthFt: number;
  description: string;
}

export interface Container {
  id: number;
  number: string;
  containerTypeId: number;
  containerTypeCode: string;
  ownershipType: OwnershipType;
  status: ContainerStatus;
}

export interface CreateContainerRequest {
  number: string;
  containerTypeId: number;
  ownershipType: OwnershipType;
  status: ContainerStatus;
}

export interface UpdateContainerRequest {
  containerTypeId: number;
  ownershipType: OwnershipType;
  status: ContainerStatus;
}

export interface ContainerListQuery {
  search: string;
  containerTypeId: number | null;
  status: ContainerStatus | '';
  sortBy: ContainerSortField;
  sortDirection: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const OWNERSHIP_LABELS: Record<OwnershipType, string> = {
  Owned: 'Owned',
  Leased: 'Leased',
};

export const STATUS_LABELS: Record<ContainerStatus, string> = {
  Available: 'Available',
  InUse: 'In use',
  UnderRepair: 'Under repair',
};
