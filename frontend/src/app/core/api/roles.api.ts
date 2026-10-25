import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AuditEntry,
  ListQuery,
  PagedResult,
  PermissionModule,
  RoleDetail,
  RoleOption,
  RolePermissions,
  RolePermissionsChange,
  RoleSummary,
  SaveRoleRequest,
} from './api.models';
import { toParams } from './http-params';

@Injectable({ providedIn: 'root' })
export class RolesApi {
  private readonly http = inject(HttpClient);

  list(query: ListQuery): Observable<PagedResult<RoleSummary>> {
    return this.http.get<PagedResult<RoleSummary>>('/api/roles', { params: toParams(query) });
  }

  assignable(): Observable<RoleOption[]> {
    return this.http.get<RoleOption[]>('/api/roles/assignable');
  }

  get(id: string): Observable<RoleDetail> {
    return this.http.get<RoleDetail>(`/api/roles/${id}`);
  }

  create(request: SaveRoleRequest): Observable<RoleDetail> {
    return this.http.post<RoleDetail>('/api/roles', request);
  }

  update(id: string, request: SaveRoleRequest): Observable<RoleDetail> {
    return this.http.put<RoleDetail>(`/api/roles/${id}`, request);
  }

  delete(id: string, reassignToRoleId?: string): Observable<void> {
    return this.http.delete<void>(`/api/roles/${id}`, { params: toParams({ reassignToRoleId }) });
  }

  permissions(id: string): Observable<RolePermissions> {
    return this.http.get<RolePermissions>(`/api/roles/${id}/permissions`);
  }

  savePermissions(id: string, permissionIds: number[]): Observable<RolePermissionsChange> {
    return this.http.put<RolePermissionsChange>(`/api/roles/${id}/permissions`, { permissionIds });
  }

  audit(id: string): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`/api/roles/${id}/audit`);
  }

  catalog(): Observable<PermissionModule[]> {
    return this.http.get<PermissionModule[]>('/api/permissions');
  }
}
