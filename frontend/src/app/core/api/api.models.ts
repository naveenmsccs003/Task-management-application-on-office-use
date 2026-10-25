export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ListQuery {
  search?: string;
  status?: '' | 'active' | 'inactive';
  sort?: string;
  direction?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export interface AuditEntry {
  id: number;
  timestampUtc: string;
  actorUserId: string | null;
  actorName: string | null;
  action: string;
  summary: string;
}

export interface Permission {
  id: number;
  key: string;
  displayName: string;
  description: string;
  parentPermissionId: number | null;
  isActive: boolean;
  allowMemberOverride: boolean;
}

export interface PermissionModule {
  key: string;
  name: string;
  icon: string;
  isImplemented: boolean;
  permissions: Permission[];
}

export interface RoleSummary {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  isSystem: boolean;
  memberCount: number;
  permissionCount: number;
  createdAtUtc: string;
  createdByName: string | null;
}

export interface RoleDetail extends RoleSummary {
  updatedAtUtc: string;
}

export interface SaveRoleRequest {
  name: string;
  description: string | null;
  isActive: boolean;
}

export interface RolePermissions {
  roleId: string;
  isSystem: boolean;
  isEditable: boolean;
  grantedPermissionIds: number[];
}

export interface RolePermissionsChange {
  added: string[];
  removed: string[];
  affectedMembers: number;
}

export interface RoleOption {
  id: string;
  name: string;
  isSystem: boolean;
  isAssignable: boolean;
}

export interface RoleReference {
  id: string;
  name: string;
  isSystem: boolean;
  isActive: boolean;
}

export interface MemberSummary {
  id: string;
  displayName: string;
  userName: string;
  email: string | null;
  role: RoleReference | null;
  isActive: boolean;
  isLockedOut: boolean;
  hasPhoto: boolean;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
}

export interface PersonalInformation {
  dateOfBirth: string | null;
  age?: number | null;
  phoneNumber: string | null;
  addressLine: string | null;
  city: string | null;
  stateOrProvince: string | null;
  postalCode: string | null;
  country: string | null;
}

export interface MemberDetail extends MemberSummary {
  lockoutEndUtc: string | null;
  hasPassword: boolean;
  updatedAtUtc: string;
  createdByName: string | null;
  isSelf: boolean;
  isAdministrator: boolean;
  canManage: boolean;
  personalInformationVisible: boolean;
  personalInformation: PersonalInformation | null;
}

export interface MemberListQuery extends ListQuery {
  roleId?: string;
}

export type SetupMethod = 'Invitation' | 'Password';

export interface CreateMemberRequest {
  displayName: string;
  email: string;
  userName: string;
  roleId: string;
  isActive: boolean;
  setupMethod: SetupMethod;
  password?: string | null;
  confirmPassword?: string | null;
  personalInformation?: PersonalInformation | null;
}

export interface UpdateMemberRequest {
  displayName?: string;
  userName?: string;
  email?: string;
  personalInformation?: PersonalInformation;
}

export type PermissionEffect = 'Allow' | 'Deny';

export interface MemberPermissionItem {
  permissionId: number;
  key: string;
  displayName: string;
  description: string;
  moduleKey: string;
  moduleName: string;
  roleDefault: boolean;
  override: PermissionEffect | null;
  effective: boolean;
  source: string;
  allowMemberOverride: boolean;
}

export interface MemberPermissions {
  memberId: string;
  roleName: string | null;
  isAdministrator: boolean;
  isEditable: boolean;
  items: MemberPermissionItem[];
}
