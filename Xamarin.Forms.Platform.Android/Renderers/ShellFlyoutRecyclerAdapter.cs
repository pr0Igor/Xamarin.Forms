﻿using Android.Runtime;
#if __ANDROID_29__
using AndroidX.AppCompat.Widget;
using AndroidX.RecyclerView.Widget;
#else
using Android.Support.V7.Widget;
#endif
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xamarin.Forms.Internals;
using AView = Android.Views.View;
using LP = Android.Views.ViewGroup.LayoutParams;

namespace Xamarin.Forms.Platform.Android
{
	public class ShellFlyoutRecyclerAdapter : RecyclerView.Adapter
	{
		readonly IShellContext _shellContext;
		List<AdapterListItem> _listItems;
		Dictionary<int, DataTemplate> _templateMap = new Dictionary<int, DataTemplate>();
		Action<Element> _selectedCallback;
		bool _disposed;

		public ShellFlyoutRecyclerAdapter(IShellContext shellContext, Action<Element> selectedCallback)
		{
			_shellContext = shellContext;

			ShellController.StructureChanged += OnShellStructureChanged;

			_listItems = GenerateItemList();
			_selectedCallback = selectedCallback;
		}

		public override int ItemCount => _listItems.Count;

		protected Shell Shell => _shellContext.Shell;

		IShellController ShellController => (IShellController)Shell;

		protected virtual DataTemplate DefaultItemTemplate => null;

		protected virtual DataTemplate DefaultMenuItemTemplate => null;

		public override int GetItemViewType(int position)
		{
			var item = _listItems[position];
			DataTemplate dataTemplate = ShellController.GetFlyoutItemDataTemplate(item.Element);
			if (item.Element is IMenuItemController)
			{
				if (DefaultMenuItemTemplate != null && Shell.MenuItemTemplate == dataTemplate)
					dataTemplate = DefaultMenuItemTemplate;
			}
			else
			{
				if (DefaultItemTemplate != null && Shell.ItemTemplate == dataTemplate)
					dataTemplate = DefaultItemTemplate;
			}

			var template = dataTemplate.SelectDataTemplate(item.Element, Shell);
			var id = ((IDataTemplateController)template).Id;

			_templateMap[id] = template;
			
			return id;
		}


		class LinearLayoutWithFocus : LinearLayout, ITabStop, IVisualElementRenderer
		{
			public LinearLayoutWithFocus(global::Android.Content.Context context) : base(context)
			{
			}

			AView ITabStop.TabStop => this;

			#region IVisualElementRenderer

			VisualElement IVisualElementRenderer.Element => Content?.BindingContext as VisualElement;

			VisualElementTracker IVisualElementRenderer.Tracker => null;

			ViewGroup IVisualElementRenderer.ViewGroup => this;

			AView IVisualElementRenderer.View => this;

			SizeRequest IVisualElementRenderer.GetDesiredSize(int widthConstraint, int heightConstraint) => new SizeRequest(new Size(100, 100));

			void IVisualElementRenderer.SetElement(VisualElement element) { }

			void IVisualElementRenderer.SetLabelFor(int? id) { }

			void IVisualElementRenderer.UpdateLayout() { }

#pragma warning disable 67
			public event EventHandler<VisualElementChangedEventArgs> ElementChanged;
			public event EventHandler<PropertyChangedEventArgs> ElementPropertyChanged;
#pragma warning restore 67

			#endregion IVisualElementRenderer

			internal View Content { get; set; }

			public override AView FocusSearch([GeneratedEnum] FocusSearchDirection direction)
			{
				var element = Content?.BindingContext as ITabStopElement;
				if (element == null)
					return base.FocusSearch(direction);

				int maxAttempts = 0;
				var tabIndexes = element?.GetTabIndexesOnParentPage(out maxAttempts);
				if (tabIndexes == null)
					return base.FocusSearch(direction);

				// use OS default--there's no need for us to keep going if there's one or fewer tab indexes!
				if (tabIndexes.Count <= 1)
					return base.FocusSearch(direction);

				int tabIndex = element.TabIndex;
				AView control = null;
				int attempt = 0;
				bool forwardDirection = !(
					(direction & FocusSearchDirection.Backward) != 0 ||
					(direction & FocusSearchDirection.Left) != 0 ||
					(direction & FocusSearchDirection.Up) != 0);

				do
				{
					element = element.FindNextElement(forwardDirection, tabIndexes, ref tabIndex);
					var renderer = (element as BindableObject).GetValue(Platform.RendererProperty);
					control = (renderer as ITabStop)?.TabStop;
				} while (!(control?.Focusable == true || ++attempt >= maxAttempts));

				return control?.Focusable == true ? control : null;
			}
		}

		public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
		{
			var item = _listItems[position];
			var elementHolder = (ElementViewHolder)holder;

			elementHolder.Bar.Visibility = item.DrawTopLine ? ViewStates.Visible : ViewStates.Gone;
			elementHolder.Element = item.Element;
		}

		public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
		{
			var template = _templateMap[viewType];

			var linearLayout = new LinearLayoutWithFocus(parent.Context)
			{
				Orientation = Orientation.Vertical,
				LayoutParameters = new RecyclerView.LayoutParams(LP.MatchParent, LP.WrapContent)
			};

			return new ElementViewHolder(template, linearLayout, _selectedCallback, _shellContext);
		}


		public override void OnViewRecycled(Java.Lang.Object holder)
		{
			base.OnViewRecycled(holder);

			if (holder is ElementViewHolder viewHolder)
			{
				viewHolder.Element = null;
			}
		}

		protected virtual List<AdapterListItem> GenerateItemList()
		{
			var result = new List<AdapterListItem>();

			var grouping = ((IShellController)_shellContext.Shell).GenerateFlyoutGrouping();

			bool skip = true;

			foreach (var sublist in grouping)
			{
				bool first = !skip;
				foreach (var element in sublist)
				{
					result.Add(new AdapterListItem(element, first));
					first = false;
				}
				skip = false;
			}

			return result;
		}

		protected virtual void OnShellStructureChanged(object sender, EventArgs e)
		{
			_listItems = GenerateItemList();
			NotifyDataSetChanged();
		}

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			_disposed = true;

			if (disposing)
			{
				((IShellController)Shell).StructureChanged -= OnShellStructureChanged;
				_listItems = null;
				_selectedCallback = null;
			}

			base.Dispose(disposing);
		}

		public class AdapterListItem
		{
			public AdapterListItem(Element element, bool drawTopLine = false)
			{
				DrawTopLine = drawTopLine;
				Element = element;
			}

			public bool DrawTopLine { get; set; }
			public Element Element { get; set; }
		}

		public class ElementViewHolder : RecyclerView.ViewHolder
		{
			Action<Element> _selectedCallback;
			Element _element;
			AView _itemView;
			bool _disposed;
			ContainerView _containerView;
			IShellContext _shellContext;
			DataTemplate _template;

			public ElementViewHolder(DataTemplate template, ViewGroup itemView, Action<Element> selectedCallback, IShellContext shellContext) : base(itemView)
			{
				_template = template;
				_shellContext = shellContext;
				var bar = new AView(itemView.Context);
				bar.SetBackgroundColor(Color.Black.MultiplyAlpha(0.14).ToAndroid());
				bar.LayoutParameters = new LP(LP.MatchParent, (int)itemView.Context.ToPixels(1));
				itemView.AddView(bar);

				var container = new ContainerView(itemView.Context, (View)null);
				container.MatchWidth = true;
				container.LayoutParameters = new LP(LP.MatchParent, LP.WrapContent);
				itemView.AddView(container);

				_containerView = container;
				_itemView = itemView;
				Bar = bar;
				_selectedCallback = selectedCallback;
			}

			public AView Bar { get; }

			public View View => _containerView?.View;
			public Element Element
			{
				get { return _element; }
				set
				{
					if (_element == value)
						return;

					if (_element != null && _element is BaseShellItem)
					{
						_element.ClearValue(Platform.RendererProperty);
						_element.PropertyChanged -= OnElementPropertyChanged;

						if (_containerView.View != null)
						{
							_containerView.View.BindingContext = null;
							_containerView.View.Parent = null;
						}

						_containerView.View = null;
					}

					if(_itemView != null)
						_itemView.Click -= OnClicked;

					_element = value;

					if (_element != null)
					{
						FastRenderers.AutomationPropertiesProvider.AccessibilitySettingsChanged(_itemView, value);
						_element.SetValue(Platform.RendererProperty, _itemView);
						_element.PropertyChanged += OnElementPropertyChanged;
						_itemView.Click += OnClicked;
						UpdateVisualState();


						var template = _template.SelectDataTemplate(_element, _element);
						var content = (View)template.CreateContent();
						content.BindingContext = value;
						content.Parent = _shellContext.Shell;
						PropertyPropagationExtensions.PropagatePropertyChanged(null, content, _element);
						_containerView.View = content;
					}
				}
			}

			void UpdateVisualState()
			{
				if (Element is BaseShellItem baseShellItem && baseShellItem != null && View != null)
				{
					if (baseShellItem.IsChecked)
						VisualStateManager.GoToState(View, "Selected");
					else
						VisualStateManager.GoToState(View, "Normal");
				}
			}

			void OnElementPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
			{
				if (e.PropertyName == BaseShellItem.IsCheckedProperty.PropertyName)
					UpdateVisualState();
			}

			void OnClicked(object sender, EventArgs e)
			{
				if (Element == null)
					return;

				_selectedCallback(Element);
			}

			protected override void Dispose(bool disposing)
			{
				if (_disposed)
					return;

				_disposed = true;

				if (disposing)
				{
					_itemView.Click -= OnClicked;
					_containerView?.Dispose();
					_containerView = null;
					Element = null;
					_itemView = null;
					_selectedCallback = null;					
				}

				base.Dispose(disposing);
			}
		}
	}
}