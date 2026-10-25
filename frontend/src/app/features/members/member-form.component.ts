import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { MemberDetail, PersonalInformation, RoleOption, SetupMethod, UpdateMemberRequest } from '../../core/api/api.models';
import { MembersApi } from '../../core/api/members.api';
import { RolesApi } from '../../core/api/roles.api';
import { PASSWORD_MIN_LENGTH } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Perm } from '../../core/auth/permissions';
import { toApiError } from '../../core/http/api-error';
import { initials } from '../../core/ui/format';
import { ToastService } from '../../core/ui/toast.service';
import { ageFrom, pastDateValidator } from './member-utils';

/** Create and edit member. Only sections the user may change are editable; the API enforces the same rules. */
@Component({
  selector: 'app-member-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './member-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MemberFormComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly rolesApi = inject(RolesApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  protected readonly auth = inject(AuthService);
  protected readonly Perm = Perm;
  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly initials = initials;

  readonly id = input<string>();

  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly form = this.fb.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    userName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
    roleId: ['', [Validators.required]],
    isActive: [true],
    setupMethod: ['Invitation' as SetupMethod],
    password: [''],
    confirmPassword: [''],
    personal: this.fb.group({
      dateOfBirth: ['', [pastDateValidator]],
      phoneNumber: ['', [Validators.maxLength(30), Validators.pattern(/^[0-9+()\-.\s]*$/)]],
      addressLine: ['', [Validators.maxLength(200)]],
      city: ['', [Validators.maxLength(100)]],
      stateOrProvince: ['', [Validators.maxLength(100)]],
      postalCode: ['', [Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9 -]*$/)]],
      country: ['', [Validators.maxLength(100)]],
    }),
  });

  protected readonly member = signal<MemberDetail | null>(null);
  protected readonly roles = signal<RoleOption[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string[]>>({});
  protected readonly photoVersion = signal(Date.now());
  protected readonly uploading = signal(false);
  /** Photo chosen while creating a member; uploaded right after the account is created. */
  protected readonly pendingPhoto = signal<File | null>(null);
  protected readonly pendingPreview = signal<string | null>(null);
  protected readonly photoError = signal<string | null>(null);

  private readonly setupMethod = toSignal(this.form.controls.setupMethod.valueChanges, { initialValue: 'Invitation' as SetupMethod });
  private readonly dob = toSignal(this.form.controls.personal.controls.dateOfBirth.valueChanges, { initialValue: '' });
  protected readonly age = computed(() => ageFrom(this.dob()));
  protected readonly usesPassword = computed(() => this.setupMethod() === 'Password');

  protected readonly isEdit = computed(() => !!this.id());
  protected readonly canEditAccount = computed(() => !this.isEdit() || this.auth.can(Perm.members.edit));
  protected readonly canEditEmail = computed(() => !this.isEdit() || this.auth.can(Perm.members.changeEmail));
  /** Editing personal information needs to see it too, so unseen values are never overwritten. */
  protected readonly canEditPersonal = computed(() =>
    this.auth.can(Perm.members.editPersonal) && (!this.isEdit() || this.auth.can(Perm.members.viewPersonal)),
  );

  ngOnInit(): void {
    const id = this.id();
    if (!id) {
      this.rolesApi.assignable().subscribe({ next: (roles) => this.roles.set(roles) });
      return;
    }
    this.form.controls.roleId.clearValidators();
    this.form.controls.roleId.updateValueAndValidity();
    this.loading.set(true);
    this.api
      .get(id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (member) => {
          this.member.set(member);
          this.form.patchValue({ displayName: member.displayName, email: member.email ?? '', userName: member.userName });
          const p = member.personalInformation;
          if (p) {
            this.form.controls.personal.patchValue({
              dateOfBirth: p.dateOfBirth ?? '', phoneNumber: p.phoneNumber ?? '', addressLine: p.addressLine ?? '',
              city: p.city ?? '', stateOrProvince: p.stateOrProvince ?? '', postalCode: p.postalCode ?? '', country: p.country ?? '',
            });
          }
          if (!this.canEditAccount()) {
            this.form.controls.displayName.disable();
            this.form.controls.userName.disable();
          }
          if (!this.canEditEmail()) this.form.controls.email.disable();
          if (!this.canEditPersonal()) this.form.controls.personal.disable();
        },
        error: (error: unknown) => this.error.set(toApiError(error, 'The member could not be loaded.').message),
      });
  }

  protected invalid(path: string): boolean {
    const control = this.form.get(path);
    const serverKey = path.replace('personal.', 'personalInformation.');
    return (!!control && control.enabled && control.invalid && (control.touched || control.dirty))
      || !!this.serverErrorsFor(serverKey).length;
  }

  protected firstError(key: string): string | undefined {
    return this.serverErrorsFor(key)[0];
  }

  protected serverErrorsFor(key: string): string[] {
    const errors = this.serverErrors();
    const lower = key.toLowerCase();
    return Object.entries(errors).filter(([k]) => k.toLowerCase() === lower).flatMap(([, v]) => v);
  }

  protected get passwordMismatch(): boolean {
    const { password, confirmPassword } = this.form.getRawValue();
    return this.usesPassword() && !!confirmPassword && password !== confirmPassword;
  }

  protected submit(): void {
    if (this.saving()) return;
    const passwordInvalid = this.usesPassword() && (this.form.controls.password.value.length < PASSWORD_MIN_LENGTH || this.passwordMismatch);
    if (this.form.invalid || (!this.isEdit() && passwordInvalid)) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.serverErrors.set({});
    const value = this.form.getRawValue();
    const personal = this.personalPayload(value.personal);
    const id = this.id();

    const request$ = id
      ? this.api.update(id, this.updatePayload(value, personal))
      : this.api.create({
          displayName: value.displayName,
          email: value.email,
          userName: value.userName,
          roleId: value.roleId,
          isActive: value.isActive,
          setupMethod: value.setupMethod,
          password: this.usesPassword() ? value.password : null,
          confirmPassword: this.usesPassword() ? value.confirmPassword : null,
          personalInformation: this.canEditPersonal() ? personal : null,
        });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (member) => {
        this.toasts.success(
          id ? 'Member updated.'
            : value.setupMethod === 'Invitation' && value.isActive ? `Member created. An invitation was emailed to ${member.email}.` : 'Member created.',
        );
        const photo = !id ? this.pendingPhoto() : null;
        if (!photo) {
          void this.router.navigate(['/members', member.id]);
          return;
        }
        this.api.uploadPhoto(member.id, photo).subscribe({
          next: () => void this.router.navigate(['/members', member.id]),
          error: (error: unknown) => {
            this.toasts.error(`Member created, but the photo was not saved: ${toApiError(error, 'upload failed').message}`);
            void this.router.navigate(['/members', member.id]);
          },
        });
      },
      error: (error: unknown) => {
        const apiError = toApiError(error, 'The member could not be saved.');
        this.serverErrors.set(apiError.fieldErrors);
        this.error.set(apiError.message);
      },
    });
  }

  protected onPhotoSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    const id = this.id();
    if (!file) return;
    if (!id) {
      this.choosePendingPhoto(file);
      (event.target as HTMLInputElement).value = '';
      return;
    }
    this.uploading.set(true);
    this.api
      .uploadPhoto(id, file)
      .pipe(finalize(() => this.uploading.set(false)))
      .subscribe({
        next: () => {
          this.member.update((m) => (m ? { ...m, hasPhoto: true } : m));
          this.photoVersion.set(Date.now());
          this.toasts.success('Profile photo updated.');
        },
        error: (error: unknown) => this.toasts.error(toApiError(error, 'The photo could not be uploaded.').message),
      });
    (event.target as HTMLInputElement).value = '';
  }

  /** Client-side pre-check only; the API validates the real file signature and size again. */
  private choosePendingPhoto(file: File): void {
    this.photoError.set(null);
    if (!['image/jpeg', 'image/png'].includes(file.type)) {
      this.photoError.set('Only JPEG and PNG images are supported.');
      return;
    }
    if (file.size > 1024 * 1024) {
      this.photoError.set('The image must be 1 MB or smaller.');
      return;
    }
    this.clearPendingPhoto();
    this.pendingPhoto.set(file);
    this.pendingPreview.set(URL.createObjectURL(file));
  }

  protected clearPendingPhoto(): void {
    const preview = this.pendingPreview();
    if (preview) URL.revokeObjectURL(preview);
    this.pendingPhoto.set(null);
    this.pendingPreview.set(null);
  }

  protected removePhoto(): void {
    const id = this.id();
    if (!id) {
      this.clearPendingPhoto();
      return;
    }
    this.api.deletePhoto(id).subscribe({
      next: () => {
        this.member.update((m) => (m ? { ...m, hasPhoto: false } : m));
        this.toasts.success('Profile photo removed.');
      },
      error: (error: unknown) => this.toasts.error(toApiError(error, 'The photo could not be removed.').message),
    });
  }

  protected photoUrl(): string {
    return this.api.photoUrl(this.id()!, this.photoVersion());
  }

  /** Sends only the sections the user may change, so field-level rules are respected. */
  private updatePayload(value: ReturnType<typeof this.form.getRawValue>, personal: PersonalInformation): UpdateMemberRequest {
    const request: UpdateMemberRequest = {};
    const member = this.member()!;
    if (this.canEditAccount() && (value.displayName !== member.displayName || value.userName !== member.userName)) {
      request.displayName = value.displayName;
      request.userName = value.userName;
    }
    if (this.canEditEmail() && value.email !== member.email) request.email = value.email;
    if (this.canEditPersonal() && this.form.controls.personal.dirty) request.personalInformation = personal;
    return request;
  }

  private personalPayload(value: ReturnType<typeof this.form.controls.personal.getRawValue>): PersonalInformation {
    const clean = (v: string) => (v.trim() ? v.trim() : null);
    return {
      dateOfBirth: value.dateOfBirth || null,
      phoneNumber: clean(value.phoneNumber),
      addressLine: clean(value.addressLine),
      city: clean(value.city),
      stateOrProvince: clean(value.stateOrProvince),
      postalCode: clean(value.postalCode),
      country: clean(value.country),
    };
  }
}
