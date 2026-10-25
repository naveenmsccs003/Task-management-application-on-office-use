import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AuditEntry,
  CreateMemberRequest,
  MemberDetail,
  MemberListQuery,
  MemberPermissions,
  MemberSummary,
  PagedResult,
  PermissionEffect,
  UpdateMemberRequest,
} from './api.models';
import { toParams } from './http-params';

@Injectable({ providedIn: 'root' })
export class MembersApi {
  private readonly http = inject(HttpClient);

  list(query: MemberListQuery): Observable<PagedResult<MemberSummary>> {
    return this.http.get<PagedResult<MemberSummary>>('/api/members', { params: toParams(query) });
  }

  export(query: MemberListQuery): Observable<Blob> {
    return this.http.get('/api/members/export', { params: toParams(query), responseType: 'blob' });
  }

  get(id: string): Observable<MemberDetail> {
    return this.http.get<MemberDetail>(`/api/members/${id}`);
  }

  create(request: CreateMemberRequest): Observable<MemberDetail> {
    return this.http.post<MemberDetail>('/api/members', request);
  }

  update(id: string, request: UpdateMemberRequest): Observable<MemberDetail> {
    return this.http.put<MemberDetail>(`/api/members/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/members/${id}`);
  }

  changeRole(id: string, roleId: string): Observable<MemberDetail> {
    return this.http.post<MemberDetail>(`/api/members/${id}/change-role`, { roleId });
  }

  setStatus(id: string, isActive: boolean): Observable<MemberDetail> {
    return this.http.post<MemberDetail>(`/api/members/${id}/status`, { isActive });
  }

  resetPassword(id: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`/api/members/${id}/reset-password`, {});
  }

  permissions(id: string): Observable<MemberPermissions> {
    return this.http.get<MemberPermissions>(`/api/members/${id}/permissions`);
  }

  savePermissions(id: string, overrides: { permissionId: number; effect: PermissionEffect }[]): Observable<MemberPermissions> {
    return this.http.put<MemberPermissions>(`/api/members/${id}/permissions`, { overrides });
  }

  audit(id: string): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`/api/members/${id}/audit`);
  }

  photoUrl(id: string, version: number): string {
    return `/api/members/${id}/photo?v=${version}`;
  }

  uploadPhoto(id: string, file: File): Observable<void> {
    const body = new FormData();
    body.append('file', file);
    return this.http.put<void>(`/api/members/${id}/photo`, body);
  }

  deletePhoto(id: string): Observable<void> {
    return this.http.delete<void>(`/api/members/${id}/photo`);
  }
}
