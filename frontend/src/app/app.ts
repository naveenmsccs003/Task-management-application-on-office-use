import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { UiHostsComponent } from './core/ui/ui-hosts.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, UiHostsComponent],
  template: '<router-outlet /><app-ui-hosts />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
